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
        string[] modes = { "Recording", "Recording", "Recording", "Transcribing", "Success", "Error" };
        string[] names = { "listening", "voice-active", "live-preview", "transcribing", "success", "error" };
        string[] titles = { "正在听你说", "正在听你说", "正在听你说", "正在整理文字", "已经放进输入框", "这次没听清" };
        string[] subtitles = { "", "", "", "中文 / English · 本地识别", "博士给你比个耶", "耳鳍抖一抖，再试一次" };
        using (Bitmap sheet = new Bitmap(752, 288, PixelFormat.Format32bppArgb))
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
                    formType.GetField("displayLevel", flags).SetValue(form, i == 1 ? 0.85f : 0.15f);
                    formType.GetField("phraseOffset", flags).SetValue(form, i == 1 ? 2 : 0);
                    if (i == 2) formType.GetField("previewText", flags).SetValue(form, "帮我 check 这个 design，然后整理今天的实验结果。");
                    paint.Invoke(form, new object[] { new PaintEventArgs(graphics, new Rectangle(0, 0, 356, 72)) });
                    bitmap.Save(Path.Combine(output, names[i] + ".png"), ImageFormat.Png);
                    sg.DrawImageUnscaled(bitmap, 12 + (i % 2) * 372, 12 + (i / 2) * 96);
                }
            }
            sheet.Save(Path.Combine(output, "white-mudkip-states.png"), ImageFormat.Png);
        }
        Console.WriteLine(Path.Combine(output, "white-mudkip-states.png"));
    }
}
