using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using TUIO;

internal sealed class GestureMessage
{
    public string Gesture { get; }
    public string User { get; }

    public GestureMessage(string gesture, string user)
    {
        Gesture = string.IsNullOrWhiteSpace(gesture) ? "NONE" : gesture.Trim();
        User = string.IsNullOrWhiteSpace(user) ? "Unknown" : user.Trim();
    }

    public static GestureMessage Parse(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return new GestureMessage("NONE", "Unknown");
        }

        string[] parts = payload.Split('|');
        string gesture = parts.Length > 0 ? parts[0] : "NONE";
        string user = parts.Length > 1 ? parts[1] : "Unknown";

        return new GestureMessage(gesture, user);
    }
}

internal sealed class WorkoutUiState
{
    public string ReceivedGesture { get; private set; } = "NONE";
    public string CurrentUser { get; private set; } = "Unknown";
    public string CurrentMarker { get; private set; } = "NONE";
    public string CurrentMode { get; private set; } = "Free Mode";
    public string Scenario { get; private set; } = "Free Mode";
    public string Feedback { get; private set; } = string.Empty;
    public string ExerciseLabel { get; private set; } = string.Empty;
    public int MenuIndex { get; private set; }
    public int PushupReps { get; private set; }
    public int SquatReps { get; private set; }
    public string BenefitText { get; private set; } = "Show a marker to choose an exercise.";
    public string PlayText { get; private set; } = "Stand in view, then follow the on-screen exercise mode.";

    private string previousGesture = "NONE";

    public void ApplyGesture(GestureMessage message)
    {
        ReceivedGesture = message.Gesture;
        CurrentUser = message.User;
        UpdateRepCounts();
        RecalculateDerivedState();
        previousGesture = ReceivedGesture;
    }

    public void ApplyMarker(int symbolId)
    {
        CurrentMarker = MapMarker(symbolId);
        RecalculateDerivedState();
    }

    public void ClearMarker()
    {
        RecalculateDerivedState();
    }

    private void RecalculateDerivedState()
    {
        CurrentMode = BuildMode(CurrentMarker);
        Feedback = BuildFeedback(ReceivedGesture);
        ExerciseLabel = BuildExerciseLabel(CurrentMode, ReceivedGesture);
        Scenario = BuildScenario(CurrentMode, ReceivedGesture);
        BenefitText = BuildBenefitText(CurrentMode);
        PlayText = BuildPlayText(CurrentMode);
    }

    public void SetMenuIndex(int menuIndex)
    {
        if (menuIndex < 0)
        {
            menuIndex = 0;
        }

        if (menuIndex >= 2)
        {
            menuIndex = 1;
        }

        MenuIndex = menuIndex;
    }

    private void UpdateRepCounts()
    {
        if (IsNewCorrectRep("pushup"))
        {
            PushupReps += 1;
        }

        if (IsNewCorrectRep("squat"))
        {
            SquatReps += 1;
        }
    }

    private bool IsNewCorrectRep(string exerciseName)
    {
        bool currentIsCorrectRep =
            ReceivedGesture.IndexOf(exerciseName, StringComparison.OrdinalIgnoreCase) >= 0
            && ReceivedGesture.IndexOf("correct", StringComparison.OrdinalIgnoreCase) >= 0;

        bool previousWasCorrectRep =
            previousGesture.IndexOf(exerciseName, StringComparison.OrdinalIgnoreCase) >= 0
            && previousGesture.IndexOf("correct", StringComparison.OrdinalIgnoreCase) >= 0;

        return currentIsCorrectRep && !previousWasCorrectRep;
    }

    private static string BuildFeedback(string gesture)
    {
        if (gesture.IndexOf("correct", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "Good Form";
        }

        if (gesture.IndexOf("wrong", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "Wrong Form";
        }

        return string.Empty;
    }

    private static string BuildExerciseLabel(string mode, string gesture)
    {
        if (mode == "Push-Up Mode")
        {
            return "Exercise: Push-Up";
        }

        if (mode == "Squat Mode")
        {
            return "Exercise: Squat";
        }

        if (gesture.IndexOf("pushup", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "Exercise: Push-Up";
        }

        if (gesture.IndexOf("squat", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "Exercise: Squat";
        }

        return string.Empty;
    }

    private static string BuildScenario(string mode, string gesture)
    {
        if (mode == "Push-Up Mode")
        {
            if (gesture.IndexOf("wrong", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "Push-Up Correction";
            }

            return "Push-Up Training";
        }

        if (mode == "Squat Mode")
        {
            if (gesture.IndexOf("wrong", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "Squat Correction";
            }

            return "Squat Training";
        }

        return "Free Mode";
    }

    private static string BuildMode(string marker)
    {
        if (marker == "Pushup Marker")
        {
            return "Push-Up Mode";
        }

        if (marker == "Squat Marker")
        {
            return "Squat Mode";
        }

        return "Free Mode";
    }

    private static string BuildBenefitText(string mode)
    {
        if (mode == "Push-Up Mode")
        {
            return "Benefits: builds chest, shoulders, triceps, and core stability.";
        }

        if (mode == "Squat Mode")
        {
            return "Benefits: strengthens quads, glutes, hamstrings, and lower-body balance.";
        }

        return "Benefits: choose a marker to load a guided exercise mode.";
    }

    private static string BuildPlayText(string mode)
    {
        if (mode == "Push-Up Mode")
        {
            return "How to play: place the pushup marker, keep your body visible, then perform push-ups until form feedback and reps update.";
        }

        if (mode == "Squat Mode")
        {
            return "How to play: place the squat marker, stand fully in frame, then perform squats and watch posture and rep counting.";
        }

        return "How to play: show a pushup or squat marker to switch modes, then start moving in front of the camera.";
    }

    private static string MapMarker(int symbolId)
    {
        switch (symbolId)
        {
            case 0:
                return "Default Marker";
            case 1:
                return "Pushup Marker";
            case 2:
                return "Squat Marker";
            default:
                return null;
        }
    }
}

internal sealed class SocketListener : IDisposable
{
    private readonly string host;
    private readonly int port;
    private readonly CancellationTokenSource cancellationSource = new CancellationTokenSource();
    private Task listeningTask;

    public event Action<GestureMessage> MessageReceived;
    public event Action<string> StatusChanged;

    public SocketListener(string host, int port)
    {
        this.host = host;
        this.port = port;
    }

    public void Start()
    {
        listeningTask = Task.Run(() => ListenLoopAsync(cancellationSource.Token));
    }

    private async Task ListenLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            TcpClient client = null;

            try
            {
                StatusChanged?.Invoke("Connecting to Python...");
                client = new TcpClient();
                await client.ConnectAsync(host, port).ConfigureAwait(false);

                StatusChanged?.Invoke("Connected to Python");

                using (client)
                using (NetworkStream stream = client.GetStream())
                using (StreamReader reader = new StreamReader(stream))
                {
                    while (!token.IsCancellationRequested)
                    {
                        string line = await reader.ReadLineAsync().ConfigureAwait(false);
                        if (line == null)
                        {
                            break;
                        }

                        MessageReceived?.Invoke(GestureMessage.Parse(line));
                    }
                }

                StatusChanged?.Invoke("Python disconnected");
            }
            catch (Exception ex)
            {
                if (token.IsCancellationRequested)
                {
                    return;
                }

                StatusChanged?.Invoke("Socket error: " + ex.Message);
            }
            finally
            {
                if (client != null)
                {
                    client.Close();
                }
            }

            try
            {
                await Task.Delay(1000, token).ConfigureAwait(false);
            }
            catch (TaskCanceledException)
            {
                return;
            }
        }
    }

    public void Dispose()
    {
        cancellationSource.Cancel();

        try
        {
            if (listeningTask != null)
            {
                listeningTask.Wait(1000);
            }
        }
        catch (AggregateException)
        {
        }

        cancellationSource.Dispose();
    }
}

public class TuioDemo : Form, TuioListener
{
    private const int WindowWidth = 800;
    private const int WindowHeight = 600;
    private const int TuioPort = 3333;
    private const string SocketHost = "127.0.0.1";
    private const int SocketPort = 5000;

    private readonly string[] menuItems = { "Start", "End" };
    private readonly Font font = new Font("Arial", 12);
    private readonly Brush textBrush = Brushes.White;
    private readonly Dictionary<long, TuioObject> objectList = new Dictionary<long, TuioObject>();
    private readonly WorkoutUiState uiState = new WorkoutUiState();

    private TuioClient client;
    private SocketListener socketListener;
    private string connectionStatus = "Waiting for Python...";

    public TuioDemo(int port)
    {
        ClientSize = new Size(WindowWidth, WindowHeight);
        Text = "Smart Fitness Interface";
        DoubleBuffered = true;

        client = new TuioClient(port);
        client.addTuioListener(this);
        client.connect();

        socketListener = new SocketListener(SocketHost, SocketPort);
        socketListener.MessageReceived += OnMessageReceived;
        socketListener.StatusChanged += OnStatusChanged;
        socketListener.Start();
    }

    private void OnMessageReceived(GestureMessage message)
    {
        if (IsDisposed)
        {
            return;
        }

        BeginInvoke((MethodInvoker)delegate
        {
            uiState.ApplyGesture(message);
            Invalidate();
        });
    }

    private void OnStatusChanged(string status)
    {
        if (IsDisposed)
        {
            return;
        }

        BeginInvoke((MethodInvoker)delegate
        {
            connectionStatus = status;
            Invalidate();
        });
    }

    public void addTuioObject(TuioObject tuioObject)
    {
        objectList[tuioObject.SessionID] = tuioObject;
        ApplyMarkerSelection(tuioObject.SymbolID);
        UpdateMenuSelectionFromRotation(tuioObject);
        Invalidate();
    }

    public void updateTuioObject(TuioObject tuioObject)
    {
        objectList[tuioObject.SessionID] = tuioObject;
        ApplyMarkerSelection(tuioObject.SymbolID);
        UpdateMenuSelectionFromRotation(tuioObject);
        Invalidate();
    }

    public void removeTuioObject(TuioObject tuioObject)
    {
        objectList.Remove(tuioObject.SessionID);
        Invalidate();
    }

    public void addTuioCursor(TuioCursor cursor) { }
    public void updateTuioCursor(TuioCursor cursor) { }
    public void removeTuioCursor(TuioCursor cursor) { }
    public void addTuioBlob(TuioBlob blob) { }
    public void updateTuioBlob(TuioBlob blob) { }
    public void removeTuioBlob(TuioBlob blob) { }
    public void refresh(TuioTime frameTime) { }

    private void ApplyMarkerSelection(int symbolId)
    {
        if (symbolId == 0)
        {
            uiState.ApplyMarker(symbolId);
            return;
        }

        if (symbolId == 1 || symbolId == 2)
        {
            uiState.ApplyMarker(symbolId);
        }
    }

    private void UpdateMenuSelectionFromRotation(TuioObject tuioObject)
    {
        float normalizedAngle = tuioObject.AngleDegrees;
        if (normalizedAngle < 0)
        {
            normalizedAngle += 360f;
        }

        int menuIndex = (int)Math.Floor(((normalizedAngle + 45f) % 360f) / 90f);
        uiState.SetMenuIndex(menuIndex);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        Graphics graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Color.Black);

        if (uiState.CurrentMode == "Push-Up Mode")
        {
            DrawPushupModeScreen(graphics);
        }
        else if (uiState.CurrentMode == "Squat Mode")
        {
            DrawSquatModeScreen(graphics);
        }
        else
        {
            DrawFreeModeScreen(graphics);
        }
    }

    private void DrawFreeModeScreen(Graphics graphics)
    {
        using (LinearGradientBrush backgroundBrush = new LinearGradientBrush(
            new Rectangle(0, 0, WindowWidth, WindowHeight),
            Color.FromArgb(12, 18, 34),
            Color.FromArgb(5, 10, 18),
            90f))
        {
            graphics.FillRectangle(backgroundBrush, 0, 0, WindowWidth, WindowHeight);
        }

        DrawHeader(graphics, "Select Exercise Mode", "Show a pushup or squat marker to begin");
        DrawCircularMenu(graphics, Color.FromArgb(90, 120, 170, 255), Color.FromArgb(220, 255, 204, 102));
        DrawSummaryPanel(graphics, Color.FromArgb(36, 53, 86));
        DrawGuidePanel(graphics, "Ready To Play", uiState.BenefitText, uiState.PlayText, Color.FromArgb(100, 170, 220));
    }

    private void DrawPushupModeScreen(Graphics graphics)
    {
        using (LinearGradientBrush backgroundBrush = new LinearGradientBrush(
            new Rectangle(0, 0, WindowWidth, WindowHeight),
            Color.FromArgb(25, 34, 70),
            Color.FromArgb(10, 18, 36),
            90f))
        {
            graphics.FillRectangle(backgroundBrush, 0, 0, WindowWidth, WindowHeight);
        }

        using (SolidBrush glowBrush = new SolidBrush(Color.FromArgb(55, 255, 170, 80)))
        {
            graphics.FillEllipse(glowBrush, 470, 70, 220, 220);
        }

        DrawHeader(graphics, "Push-Up Mode", "Upper body focus with live posture correction");
        DrawCircularMenu(graphics, Color.FromArgb(90, 255, 170, 80), Color.FromArgb(255, 255, 204, 120));
        DrawSummaryPanel(graphics, Color.FromArgb(79, 44, 30));
        DrawGuidePanel(graphics, "Push-Up Benefits", uiState.BenefitText, uiState.PlayText, Color.FromArgb(255, 170, 80));
        DrawRepBadge(graphics, "Push-Up Reps", uiState.PushupReps.ToString(), new Rectangle(565, 385, 170, 120), Color.FromArgb(255, 170, 80));
    }

    private void DrawSquatModeScreen(Graphics graphics)
    {
        using (LinearGradientBrush backgroundBrush = new LinearGradientBrush(
            new Rectangle(0, 0, WindowWidth, WindowHeight),
            Color.FromArgb(14, 52, 42),
            Color.FromArgb(7, 20, 18),
            90f))
        {
            graphics.FillRectangle(backgroundBrush, 0, 0, WindowWidth, WindowHeight);
        }

        using (SolidBrush glowBrush = new SolidBrush(Color.FromArgb(55, 80, 220, 170)))
        {
            graphics.FillEllipse(glowBrush, 470, 70, 220, 220);
        }

        DrawHeader(graphics, "Squat Mode", "Lower body strength and balance guidance");
        DrawCircularMenu(graphics, Color.FromArgb(90, 80, 220, 170), Color.FromArgb(255, 170, 255, 220));
        DrawSummaryPanel(graphics, Color.FromArgb(25, 72, 63));
        DrawGuidePanel(graphics, "Squat Benefits", uiState.BenefitText, uiState.PlayText, Color.FromArgb(80, 220, 170));
        DrawRepBadge(graphics, "Squat Reps", uiState.SquatReps.ToString(), new Rectangle(565, 385, 170, 120), Color.FromArgb(80, 220, 170));
    }

    private void DrawHeader(Graphics graphics, string title, string subtitle)
    {
        using (SolidBrush titleBrush = new SolidBrush(Color.White))
        using (SolidBrush subBrush = new SolidBrush(Color.FromArgb(210, 220, 228, 238)))
        using (Font titleFont = new Font("Segoe UI", 22, FontStyle.Bold))
        using (Font subFont = new Font("Segoe UI", 11, FontStyle.Regular))
        {
            graphics.DrawString(title, titleFont, titleBrush, 24, 20);
            graphics.DrawString(subtitle, subFont, subBrush, 28, 62);
        }
    }

    private void DrawCircularMenu(Graphics graphics, Color ringColor, Color activeColor)
    {
        int centerX = 210;
        int centerY = 300;
        int outerRadius = 130;
        int innerRadius = 62;
        float sectionAngle = 360f / menuItems.Length;

        for (int i = 0; i < menuItems.Length; i++)
        {
            float startAngle = -90f + (i * sectionAngle);
            Color fillColor = i == uiState.MenuIndex ? activeColor : ringColor;

            using (GraphicsPath path = new GraphicsPath())
            using (SolidBrush fillBrush = new SolidBrush(fillColor))
            using (Pen borderPen = new Pen(Color.FromArgb(110, 255, 255, 255), 1.5f))
            {
                path.AddPie(centerX - outerRadius, centerY - outerRadius, outerRadius * 2, outerRadius * 2, startAngle, sectionAngle - 3f);
                path.AddEllipse(centerX - innerRadius, centerY - innerRadius, innerRadius * 2, innerRadius * 2);
                graphics.FillPath(fillBrush, path);
                graphics.DrawPie(borderPen, centerX - outerRadius, centerY - outerRadius, outerRadius * 2, outerRadius * 2, startAngle, sectionAngle - 3f);
            }

            double angle = ((startAngle + (sectionAngle / 2f)) * Math.PI) / 180.0;
            int textX = centerX + (int)(Math.Cos(angle) * 92);
            int textY = centerY + (int)(Math.Sin(angle) * 92);
            Brush textBrush = i == uiState.MenuIndex ? Brushes.Black : Brushes.White;

            graphics.DrawString(menuItems[i], font, textBrush, textX - 18, textY - 8);
        }

        using (SolidBrush centerBrush = new SolidBrush(Color.FromArgb(225, 14, 18, 28)))
        using (Pen centerPen = new Pen(Color.FromArgb(90, 255, 255, 255), 2f))
        using (Brush modeBrush = new SolidBrush(Color.White))
        {
            graphics.FillEllipse(centerBrush, centerX - innerRadius, centerY - innerRadius, innerRadius * 2, innerRadius * 2);
            graphics.DrawEllipse(centerPen, centerX - innerRadius, centerY - innerRadius, innerRadius * 2, innerRadius * 2);
            graphics.DrawString(uiState.CurrentMode.Replace(" Mode", ""), font, modeBrush, centerX - 32, centerY - 10);
        }
    }

    private void DrawSummaryPanel(Graphics graphics, Color accentColor)
    {
        Rectangle panel = new Rectangle(24, 435, 320, 135);

        using (SolidBrush panelBrush = new SolidBrush(Color.FromArgb(170, 12, 16, 24)))
        using (SolidBrush accentBrush = new SolidBrush(accentColor))
        using (Pen borderPen = new Pen(Color.FromArgb(90, 255, 255, 255), 1.5f))
        {
            graphics.FillRectangle(panelBrush, panel);
            graphics.FillRectangle(accentBrush, panel.X, panel.Y, 8, panel.Height);
            graphics.DrawRectangle(borderPen, panel);
        }

        graphics.DrawString("Status: " + connectionStatus, font, Brushes.LightBlue, panel.X + 20, panel.Y + 16);
        graphics.DrawString("User: " + uiState.CurrentUser, font, Brushes.White, panel.X + 20, panel.Y + 42);
        graphics.DrawString("Marker: " + uiState.CurrentMarker, font, Brushes.Gainsboro, panel.X + 20, panel.Y + 68);
        graphics.DrawString("Gesture: " + uiState.ReceivedGesture, font, Brushes.WhiteSmoke, panel.X + 20, panel.Y + 94);
    }

    private void DrawGuidePanel(Graphics graphics, string title, string benefits, string playText, Color accentColor)
    {
        Rectangle panel = new Rectangle(360, 95, 390, 255);

        using (SolidBrush panelBrush = new SolidBrush(Color.FromArgb(170, 14, 20, 30)))
        using (Pen borderPen = new Pen(Color.FromArgb(100, accentColor), 2f))
        using (SolidBrush titleBrush = new SolidBrush(Color.White))
        using (SolidBrush bodyBrush = new SolidBrush(Color.FromArgb(228, 235, 240, 245)))
        {
            graphics.FillRectangle(panelBrush, panel);
            graphics.DrawRectangle(borderPen, panel);
            graphics.DrawString(title, new Font("Segoe UI", 15, FontStyle.Bold), titleBrush, panel.X + 18, panel.Y + 18);
            graphics.DrawString(benefits, font, bodyBrush, new RectangleF(panel.X + 18, panel.Y + 62, panel.Width - 36, 70));
            graphics.DrawString(playText, font, bodyBrush, new RectangleF(panel.X + 18, panel.Y + 145, panel.Width - 36, 88));
        }
    }

    private void DrawRepBadge(Graphics graphics, string title, string value, Rectangle bounds, Color accentColor)
    {
        using (SolidBrush panelBrush = new SolidBrush(Color.FromArgb(175, 14, 20, 30)))
        using (Pen borderPen = new Pen(Color.FromArgb(120, accentColor), 2f))
        using (SolidBrush titleBrush = new SolidBrush(Color.White))
        using (SolidBrush valueBrush = new SolidBrush(accentColor))
        using (Font valueFont = new Font("Segoe UI", 28, FontStyle.Bold))
        {
            graphics.FillRectangle(panelBrush, bounds);
            graphics.DrawRectangle(borderPen, bounds);
            graphics.DrawString(title, font, titleBrush, bounds.X + 16, bounds.Y + 18);
            graphics.DrawString(value, valueFont, valueBrush, bounds.X + 18, bounds.Y + 46);
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        if (socketListener != null)
        {
            socketListener.Dispose();
            socketListener = null;
        }

        if (client != null)
        {
            client.disconnect();
            client = null;
        }

        font.Dispose();
        base.OnFormClosed(e);
    }

    [STAThread]
    public static void Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new TuioDemo(TuioPort));
    }
}
