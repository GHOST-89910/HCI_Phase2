import socket
import threading
from pathlib import Path

import cv2
import mediapipe as mp
import numpy as np
from dollarpy import Point, Recognizer, Template

HOST = "127.0.0.1"
PORT = 5000
CAMERA_INDEX = 0
FRAME_BATCH_SIZE = 45
POSE_JOINTS = [11, 13, 15, 23, 25, 27]
WINDOW_TITLE = "Smart Fitness System"
DATASET_DIR = Path(__file__).resolve().parent / "Dataset"
TEXT_STYLE = (cv2.FONT_HERSHEY_SIMPLEX, 0.8, (255, 255, 255), 2)
POSE_INDICES = {
    "hip": 23,
    "knee": 25,
    "ankle": 27,
    "shoulder": 11,
    "elbow": 13,
    "wrist": 15,
}

TEMPLATE_FILES = {
    "pushup_correct": DATASET_DIR / "pushups" / "correct.mp4",
    "pushup_wrong": DATASET_DIR / "pushups" / "wrong.mp4",
    "squat_correct": DATASET_DIR / "squats" / "correct.mp4",
    "squat_wrong": DATASET_DIR / "squats" / "wrong.mp4",
}

SQUAT_CORRECT_KNEE_RANGE = (65, 175)
PUSHUP_CORRECT_ELBOW_RANGE = (55, 175)


server = None
connection = None
connection_lock = threading.Lock()

recognizer = None
templates_ready = False
template_error = None


def close_quietly(resource):
    if resource is not None:
        try:
            resource.close()
        except OSError:
            pass


def start_socket_server():
    global server

    server = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    server.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    server.bind((HOST, PORT))
    server.listen(1)

    print("Waiting for C# connection on %s:%s..." % (HOST, PORT))

    threading.Thread(target=accept_client_loop, daemon=True).start()


def accept_client_loop():
    global connection

    while server is not None:
        try:
            client_socket, address = server.accept()
        except OSError:
            return

        with connection_lock:
            if connection is not None:
                close_quietly(connection)
            connection = client_socket

        print("Connected to C#:", address)


def send_label(label, user_id="user1"):
    global connection

    payload = ("%s|%s\n" % (label, user_id)).encode("utf-8")

    with connection_lock:
        if connection is None:
            return

        try:
            connection.sendall(payload)
        except OSError as exc:
            print("Socket send failed:", exc)
            close_quietly(connection)
            connection = None


def close_socket_server():
    global server
    global connection

    with connection_lock:
        close_quietly(connection)
        connection = None

    close_quietly(server)
    server = None


def load_templates():
    global recognizer
    global templates_ready
    global template_error

    print("Loading templates...")

    try:
        with mp.solutions.holistic.Holistic() as holistic:
            templates = [
                Template(name, extract_template_points(video_path, holistic))
                for name, video_path in TEMPLATE_FILES.items()
            ]

        recognizer = Recognizer(templates)
        templates_ready = True
        print("Templates loaded.")
    except Exception as exc:
        template_error = str(exc)
        print("Template loading failed:", exc)


def extract_template_points(video_path, holistic):
    if not video_path.exists():
        raise FileNotFoundError("Missing template video: %s" % video_path)

    cap = cv2.VideoCapture(str(video_path))
    gesture_frames = []

    try:
        while True:
            ret, frame = cap.read()
            if not ret:
                break

            rgb = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
            result = holistic.process(rgb)

            if not result.pose_landmarks:
                continue

            gesture_frames.append(extract_joint_coordinates(result.pose_landmarks.landmark))
    finally:
        cap.release()

    points = build_gesture_points(gesture_frames)

    if not points:
        raise ValueError(
            "No pose landmarks found in template video: %s" % video_path)

    return points


def calculate_angle(a, b, c):
    point_a, point_b, point_c = map(np.array, (a, b, c))

    radians = np.arctan2(point_c[1] - point_b[1], point_c[0] - point_b[0]) - np.arctan2(
        point_a[1] - point_b[1], point_a[0] - point_b[0]
    )
    angle = np.abs(radians * 180.0 / np.pi)
    return int(360 - angle if angle > 180 else angle)


def extract_joint_coordinates(landmarks):
    return [(landmarks[joint_id].x, landmarks[joint_id].y) for joint_id in POSE_JOINTS]


def build_gesture_points(gesture_frames):
    return [
        Point(x_coord, y_coord, joint_index)
        for joint_index in range(len(POSE_JOINTS))
        for frame_points in gesture_frames
        for x_coord, y_coord in [frame_points[joint_index]]
    ]


def get_path_length(points):
    distance = 0.0

    for index in range(1, len(points)):
        current_point = points[index]
        previous_point = points[index - 1]

        if current_point.stroke_id != previous_point.stroke_id:
            continue

        distance += float(
            np.hypot(
                current_point.x - previous_point.x,
                current_point.y - previous_point.y,
            )
        )

    return distance


def get_point_span(points):
    if not points:
        return 0.0

    x_values = [point.x for point in points]
    y_values = [point.y for point in points]
    return max(max(x_values) - min(x_values), max(y_values) - min(y_values))


def is_valid_gesture(points):
    return (
        len(points) >= len(POSE_JOINTS) * 2
        and get_path_length(points) > 1e-6
        and get_point_span(points) > 1e-6
    )


def classify_posture_by_angles(knee_angle, elbow_angle, recognized_label):
    normalized_label = recognized_label.lower()

    if "squat" in normalized_label:
        if SQUAT_CORRECT_KNEE_RANGE[0] <= knee_angle <= SQUAT_CORRECT_KNEE_RANGE[1]:
            return "squat_correct"
        return "squat_wrong"

    if "pushup" in normalized_label:
        if PUSHUP_CORRECT_ELBOW_RANGE[0] <= elbow_angle <= PUSHUP_CORRECT_ELBOW_RANGE[1]:
            return "pushup_correct"
        return "pushup_wrong"

    return recognized_label


def get_feedback_color(label):
    if "correct" in label:
        return (0, 255, 0)
    if "wrong" in label:
        return (0, 0, 255)
    return (0, 255, 255)


def draw_pose_overlay(frame, pose_landmarks, mp_drawing, mp_pose):
    mp_drawing.draw_landmarks(
        frame,
        pose_landmarks,
        mp_pose.POSE_CONNECTIONS,
        mp_drawing.DrawingSpec(color=(0, 255, 255),
                               thickness=2, circle_radius=3),
        mp_drawing.DrawingSpec(color=(255, 0, 0), thickness=2),
    )


def draw_text(frame, text, position, color, scale=0.8):
    font, _, _, thickness = TEXT_STYLE
    cv2.putText(frame, text, position, font, scale, color, thickness)


def get_landmark_xy(landmarks, name):
    point = landmarks[POSE_INDICES[name]]
    return [point.x, point.y]


def recognize_gesture(gesture_frames, knee_angle, elbow_angle):
    gesture_points = build_gesture_points(gesture_frames)

    if not is_valid_gesture(gesture_points):
        print("Skipping recognition: gesture batch has too little motion.")
        return None

    recognized_label, score = recognizer.recognize(gesture_points)
    if not recognized_label:
        print("Recognition returned no confident match.")
        return None

    recognized_label = classify_posture_by_angles(
        knee_angle,
        elbow_angle,
        recognized_label,
    )
    print("Detected:", recognized_label, "Score:", score)
    send_label(recognized_label)
    return recognized_label


def main():
    start_socket_server()
    threading.Thread(target=load_templates, daemon=True).start()

    cap = cv2.VideoCapture(CAMERA_INDEX, cv2.CAP_DSHOW)
    if not cap.isOpened():
        close_socket_server()
        raise RuntimeError("Camera failed to open.")

    print("Camera opened.")

    mp_drawing = mp.solutions.drawing_utils
    mp_pose = mp.solutions.pose

    gesture_frames = []
    frame_counter = 0
    last_label = "Loading AI..."

    try:
        with mp.solutions.holistic.Holistic() as holistic:
            while True:
                ret, frame = cap.read()
                if not ret:
                    print("Camera frame read failed.")
                    break

                frame = cv2.flip(frame, 1)

                rgb = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
                result = holistic.process(rgb)

                label = "Template load failed" if template_error else (
                    last_label if templates_ready else "Loading AI..."
                )

                if result.pose_landmarks:
                    landmarks = result.pose_landmarks.landmark
                    draw_pose_overlay(frame, result.pose_landmarks, mp_drawing, mp_pose)

                    knee_angle = calculate_angle(
                        get_landmark_xy(landmarks, "hip"),
                        get_landmark_xy(landmarks, "knee"),
                        get_landmark_xy(landmarks, "ankle"),
                    )
                    elbow_angle = calculate_angle(
                        get_landmark_xy(landmarks, "shoulder"),
                        get_landmark_xy(landmarks, "elbow"),
                        get_landmark_xy(landmarks, "wrist"),
                    )

                    draw_text(frame, "Knee: %s" % knee_angle, (30, 100), (255, 255, 255))
                    draw_text(frame, "Elbow: %s" % elbow_angle, (30, 130), (255, 255, 255))

                    if templates_ready and recognizer is not None:
                        gesture_frames.append(extract_joint_coordinates(landmarks))
                        frame_counter += 1

                        if frame_counter >= FRAME_BATCH_SIZE:
                            try:
                                recognized_label = recognize_gesture(
                                    gesture_frames, knee_angle, elbow_angle
                                )
                                if recognized_label:
                                    label = recognized_label
                                    last_label = recognized_label
                            except Exception as exc:
                                print("Recognition failed:", exc)
                            finally:
                                gesture_frames = []
                                frame_counter = 0
                else:
                    gesture_frames = []
                    frame_counter = 0

                draw_text(frame, label, (30, 50), get_feedback_color(label), 1)
                cv2.imshow(WINDOW_TITLE, frame)

                if cv2.waitKey(1) & 0xFF == ord("q"):
                    print("Exiting...")
                    break
    finally:
        cap.release()
        close_socket_server()
        cv2.destroyAllWindows()


if __name__ == "__main__":
    main()
