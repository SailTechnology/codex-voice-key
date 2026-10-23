using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

internal static class RenderOverlay
{
    [STAThread]
    private static void Main(string[] args)
    {
        string output = args[1];
        Directory.CreateDirectory(output);
        Assembly bridge = Assembly.LoadFrom(args[0]);
        Type formType = bridge.GetType("CodexVoiceKey.VoiceOverlay+OverlayForm", true);
        Type modeType = bridge.GetType("CodexVoiceKey.VoiceOverlay+OverlayMode", true);
        BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        MethodInfo paint = formType.GetMethod("OnPaint", flags);
        string[] modes = { "Recording", "Transcribing", "Success", "Error" };
        string[] titles = { "正在听你说", "正在整理文字", "已经放进输入框", "这次没听清" };
        string[] subtitles = { "松开 Ctrl+Alt+Space 结束", "中文 / English · 本地识别", "这一句完成了", "调整一下再试" };
        using (Bitmap sheet = new Bitmap(752, 192, PixelFormat.Format32bppArgb))
        using (Graphics sg = Graphics.FromImage(sheet))
        {
            sg.Clear(Color.FromArgb(236, 241, 238));
            for (int i = 0; i < modes.Length; i++)
            {
                using (Form form = (Form)Activator.CreateInstance(formType, true))
                using (Bitmap bitmap = new Bitmap(356, 72, PixelFormat.Format32bppArgb))
                using (Graphics graphics = Graphics.FromImage(bitmap))
                {
                    graphics.Clear(Color.Transparent);
                    formType.GetField("mode", flags).SetValue(form, Enum.Parse(modeType, modes[i]));
                    formType.GetField("title", flags).SetValue(form, titles[i]);
                    formType.GetField("subtitle", flags).SetValue(form, subtitles[i]);
                    paint.Invoke(form, new object[] { new PaintEventArgs(graphics, new Rectangle(0, 0, 356, 72)) });
                    bitmap.Save(Path.Combine(output, modes[i].ToLowerInvariant() + ".png"), ImageFormat.Png);
                    sg.DrawImageUnscaled(bitmap, 12 + (i % 2) * 372, 12 + (i / 2) * 96);
                }
            }
            sheet.Save(Path.Combine(output, "white-mudkip-states.png"), ImageFormat.Png);
        }
        Console.WriteLine(Path.Combine(output, "white-mudkip-states.png"));
    }
}
