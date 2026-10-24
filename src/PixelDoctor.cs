using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace CodexVoiceKey
{
    internal enum DoctorMood { Listening, Thinking, Happy, Oops, Neutral }

    // The four expressions share an embedded, transparent 2 x 2 sprite sheet.
    internal static class PixelDoctor
    {
        private static readonly Bitmap Sprites = LoadSprites();

        private static Bitmap LoadSprites()
        {
            using (Stream stream = typeof(PixelDoctor).Assembly.GetManifestResourceStream("CodexVoiceKey.MudkipStates.png"))
            {
                if (stream == null) throw new InvalidOperationException("Missing embedded Mudkip sprite sheet.");
                using (Image image = Image.FromStream(stream))
                    return new Bitmap(image);
            }
        }

        public static void Draw(Graphics graphics, Rectangle bounds, DoctorMood mood, double elapsedMs, float voiceLevel = 0)
        {
            int frame = 0;
            if (mood == DoctorMood.Thinking) frame = 1;
            else if (mood == DoctorMood.Happy) frame = 2;
            else if (mood == DoctorMood.Oops) frame = 3;
            else if (mood == DoctorMood.Listening && elapsedMs % 3900 > 3710) frame = 1;

            int cellWidth = Sprites.Width / 2;
            int cellHeight = Sprites.Height / 2;
            Rectangle source = new Rectangle((frame % 2) * cellWidth,
                (frame / 2) * cellHeight, cellWidth, cellHeight);
            int size = Math.Min(bounds.Width, bounds.Height);
            int bob = 0;
            int sway = 0;
            if (mood == DoctorMood.Listening)
            {
                bob = (int)Math.Round(Math.Sin(elapsedMs / 360.0) * (0.7 + voiceLevel * 1.8));
                sway = (int)Math.Round(Math.Sin(elapsedMs / 1200.0));
            }
            else if (mood == DoctorMood.Thinking) bob = (int)Math.Round(Math.Sin(elapsedMs / 540.0));
            else if (mood == DoctorMood.Happy && elapsedMs < 1000)
                bob = -(int)Math.Round(3 * Math.Abs(Math.Sin(elapsedMs * Math.PI / 380)) * (1 - elapsedMs / 1000));
            Rectangle target = new Rectangle(bounds.Left + (bounds.Width - size) / 2,
                bounds.Top + (bounds.Height - size) / 2 + bob, size, size);
            target.X += sway;
            GraphicsState saved = graphics.Save();
            try
            {
                graphics.CompositingQuality = CompositingQuality.HighQuality;
                graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
                graphics.PixelOffsetMode = PixelOffsetMode.Half;
                using (ImageAttributes attributes = new ImageAttributes())
                {
                    attributes.SetWrapMode(WrapMode.TileFlipXY);
                    graphics.DrawImage(Sprites, target, source.Left, source.Top,
                        source.Width, source.Height, GraphicsUnit.Pixel, attributes);
                }
            }
            finally { graphics.Restore(saved); }
        }
    }
}
