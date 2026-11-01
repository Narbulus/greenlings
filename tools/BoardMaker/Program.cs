using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

public static class RefBoards
{
    static readonly Color Bg = Color.FromArgb(231, 227, 218);
    static readonly Color Panel = Color.FromArgb(246, 243, 236);
    static readonly Color Ink = Color.FromArgb(40, 44, 38);
    static readonly Color Muted = Color.FromArgb(120, 118, 110);

    // ---- pixels -------------------------------------------------------------------------

    static byte[] Read(Bitmap b, out int stride)
    {
        var d = b.LockBits(new Rectangle(0, 0, b.Width, b.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        stride = d.Stride;
        var buf = new byte[d.Stride * b.Height];
        Marshal.Copy(d.Scan0, buf, 0, buf.Length);
        b.UnlockBits(d);
        return buf;
    }

    static Bitmap Write(byte[] buf, int w, int h)
    {
        var b = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        var d = b.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        Marshal.Copy(buf, 0, d.Scan0, buf.Length);
        b.UnlockBits(d);
        return b;
    }

    /// The game's creature shader writes alpha 0; the render was cleared to black, so any
    /// non-black pixel is the model. Rebuild alpha from colour, then crop to the model.
    public static Bitmap Matte(string path)
    {
        using (var src = new Bitmap(path))
        {
            int stride;
            var buf = Read(src, out stride);
            for (int i = 0; i < buf.Length; i += 4)
            {
                int m = Math.Max(buf[i], Math.Max(buf[i + 1], buf[i + 2]));
                int a = m > 2 ? 255 : 0;
                buf[i + 3] = (byte)Math.Max(buf[i + 3], a);
            }
            using (var full = Write(buf, src.Width, src.Height)) return Crop(full, 6);
        }
    }

    static Bitmap Crop(Bitmap b, int pad)
    {
        int stride;
        var buf = Read(b, out stride);
        int x0 = b.Width, y0 = b.Height, x1 = -1, y1 = -1;
        for (int y = 0; y < b.Height; y++)
            for (int x = 0; x < b.Width; x++)
                if (buf[y * stride + x * 4 + 3] > 0) { x0 = Math.Min(x0, x); y0 = Math.Min(y0, y); x1 = Math.Max(x1, x); y1 = Math.Max(y1, y); }
        if (x1 < 0) return new Bitmap(b);
        x0 = Math.Max(0, x0 - pad); y0 = Math.Max(0, y0 - pad);
        x1 = Math.Min(b.Width - 1, x1 + pad); y1 = Math.Min(b.Height - 1, y1 + pad);
        return b.Clone(new Rectangle(x0, y0, x1 - x0 + 1, y1 - y0 + 1), PixelFormat.Format32bppArgb);
    }

    /// Dominant colours of the opaque pixels, merged so near-duplicates don't repeat.
    public static List<Color> Palette(IEnumerable<Bitmap> images, int count)
    {
        var hist = new Dictionary<int, int>();
        foreach (var b in images)
        {
            int stride;
            var buf = Read(b, out stride);
            for (int i = 0; i < buf.Length; i += 4)
            {
                if (buf[i + 3] == 0) continue;
                int key = ((buf[i + 2] >> 3) << 10) | ((buf[i + 1] >> 3) << 5) | (buf[i] >> 3);
                int c; hist.TryGetValue(key, out c); hist[key] = c + 1;
            }
        }
        var picked = new List<Color>();
        foreach (var kv in hist.OrderByDescending(k => k.Value))
        {
            var col = Color.FromArgb(((kv.Key >> 10) & 31) * 8 + 4, ((kv.Key >> 5) & 31) * 8 + 4, (kv.Key & 31) * 8 + 4);
            if (picked.All(p => Dist(p, col) > 42)) picked.Add(col);
            if (picked.Count >= count) break;
        }
        return picked;
    }

    static double Dist(Color a, Color b) =>
        Math.Sqrt(Math.Pow(a.R - b.R, 2) + Math.Pow(a.G - b.G, 2) + Math.Pow(a.B - b.B, 2));

    // ---- drawing ------------------------------------------------------------------------

    static Graphics G(Bitmap b)
    {
        var g = Graphics.FromImage(b);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        return g;
    }

    static void Cell(Graphics g, Rectangle r, string label)
    {
        using (var br = new SolidBrush(Panel)) g.FillRectangle(br, r);
        using (var f = new Font("Segoe UI Semibold", 15f, GraphicsUnit.Pixel))
        using (var br = new SolidBrush(Muted))
            g.DrawString(label.ToUpperInvariant(), f, br, r.X + 16, r.Bottom - 30);
    }

    static void Fit(Graphics g, Image img, Rectangle box, float scale)
    {
        float w = img.Width * scale, h = img.Height * scale;
        g.DrawImage(img, box.X + (box.Width - w) / 2f, box.Bottom - h, w, h); // stand on the floor
    }

    static void FitCenter(Graphics g, Image img, Rectangle box)
    {
        float s = Math.Min((float)box.Width / img.Width, (float)box.Height / img.Height);
        float w = img.Width * s, h = img.Height * s;
        g.DrawImage(img, box.X + (box.Width - w) / 2f, box.Y + (box.Height - h) / 2f, w, h);
    }

    public static void Board(string title, string subtitle, string[] viewNames, Bitmap[] views, Bitmap[] heads, string[] headNames,
                             Bitmap texture, string textureLabel, List<Color> palette, string footer, string outPath)
    {
        const int W = 2400, H = 1640, M = 40, Gap = 20;
        int cw = (W - 2 * M - 3 * Gap) / 4, ch = 700;
        using (var bmp = new Bitmap(W, H, PixelFormat.Format32bppArgb))
        using (var g = G(bmp))
        {
            g.Clear(Bg);
            using (var f = new Font("Segoe UI", 44f, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var br = new SolidBrush(Ink)) g.DrawString(title, f, br, M - 4, 22);
            using (var f = new Font("Segoe UI", 19f, GraphicsUnit.Pixel))
            using (var br = new SolidBrush(Muted)) g.DrawString(subtitle, f, br, M, 84);

            // turnaround: one shared scale so the four views line up
            int top = 130;
            float scale = views.Select(v => Math.Min((cw - 40f) / v.Width, (ch - 70f) / v.Height)).Min();
            for (int i = 0; i < 4; i++)
            {
                var r = new Rectangle(M + i * (cw + Gap), top, cw, ch);
                Cell(g, r, viewNames[i]);
                Fit(g, views[i], new Rectangle(r.X + 20, r.Y + 20, r.Width - 40, r.Height - 60), scale);
            }

            // heads, texture, palette
            int top2 = top + ch + Gap, ch2 = H - top2 - 70;
            for (int i = 0; i < heads.Length && i < 2; i++)
            {
                var r = new Rectangle(M + i * (cw + Gap), top2, cw, ch2);
                Cell(g, r, headNames[i]);
                FitCenter(g, heads[i], new Rectangle(r.X + 20, r.Y + 20, r.Width - 40, r.Height - 70));
            }
            var tr = new Rectangle(M + 2 * (cw + Gap), top2, cw, ch2);
            Cell(g, tr, textureLabel);
            if (texture != null) FitCenter(g, texture, new Rectangle(tr.X + 30, tr.Y + 30, tr.Width - 60, tr.Height - 90));

            var pr = new Rectangle(M + 3 * (cw + Gap), top2, cw, ch2);
            Cell(g, pr, "palette (sampled from renders)");
            int sw = (pr.Width - 60) / 2, sh = Math.Min(70, (pr.Height - 90) / 4 - 14);
            using (var f = new Font("Consolas", 16f, GraphicsUnit.Pixel))
                for (int i = 0; i < palette.Count && i < 8; i++)
                {
                    int x = pr.X + 20 + (i % 2) * (sw + 20), y = pr.Y + 20 + (i / 2) * (sh + 14);
                    using (var br = new SolidBrush(palette[i])) g.FillRectangle(br, x, y, sw, sh);
                    var c = palette[i];
                    var hex = string.Format("#{0:X2}{1:X2}{2:X2}", c.R, c.G, c.B);
                    bool light = c.R * 0.3 + c.G * 0.59 + c.B * 0.11 > 140;
                    using (var br = new SolidBrush(light ? Ink : Color.White)) g.DrawString(hex, f, br, x + 8, y + sh - 24);
                }

            using (var f = new Font("Segoe UI", 15f, GraphicsUnit.Pixel))
            using (var br = new SolidBrush(Muted)) g.DrawString(footer, f, br, M, H - 40);
            bmp.Save(outPath, ImageFormat.Png);
        }
    }

    public static void Lineup(string[] names, Bitmap[] fronts, double[] heights, string outPath)
    {
        const int W = 2400, H = 1300, M = 60, Floor = 1150;
        double tallest = heights.Max();
        double ppm = (Floor - 160) / tallest; // pixels per metre
        using (var bmp = new Bitmap(W, H, PixelFormat.Format32bppArgb))
        using (var g = G(bmp))
        {
            g.Clear(Bg);
            using (var f = new Font("Segoe UI", 44f, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var br = new SolidBrush(Ink)) g.DrawString("Greydwarf family - lineup at relative scale", f, br, M - 4, 22);
            using (var f = new Font("Segoe UI", 19f, GraphicsUnit.Pixel))
            using (var br = new SolidBrush(Muted))
                g.DrawString("Front views in idle pose, scaled by in-game bounding height. Dashed line: Viking player height (~1.8 m).", f, br, M, 84);

            // metre grid
            using (var pen = new Pen(Color.FromArgb(205, 200, 190), 1f))
            using (var f = new Font("Segoe UI", 14f, GraphicsUnit.Pixel))
            using (var br = new SolidBrush(Muted))
                for (int m = 0; m <= (int)Math.Ceiling(tallest); m++)
                {
                    float y = (float)(Floor - m * ppm);
                    g.DrawLine(pen, M, y, W - M, y);
                    g.DrawString(m + " m", f, br, M, y - 20);
                }
            using (var pen = new Pen(Color.FromArgb(180, 90, 60), 2f) { DashStyle = DashStyle.Dash })
            {
                float y = (float)(Floor - 1.8 * ppm);
                g.DrawLine(pen, M, y, W - M, y);
            }
            using (var pen = new Pen(Ink, 3f)) g.DrawLine(pen, M, Floor, W - M, Floor);

            int slot = (W - 2 * M - 80) / names.Length;
            for (int i = 0; i < names.Length; i++)
            {
                var img = fronts[i];
                float h = (float)(heights[i] * ppm), w = img.Width * h / img.Height;
                float cx = M + 80 + slot * i + slot / 2f;
                g.DrawImage(img, cx - w / 2f, Floor - h, w, h);
                using (var f = new Font("Segoe UI Semibold", 22f, GraphicsUnit.Pixel))
                using (var br = new SolidBrush(Ink))
                {
                    var label = names[i] + "  " + heights[i].ToString("0.00") + " m";
                    var sz = g.MeasureString(label, f);
                    g.DrawString(label, f, br, cx - sz.Width / 2f, Floor + 14);
                }
            }
            bmp.Save(outPath, ImageFormat.Png);
        }
    }

    public static Bitmap Load(string path) { using (var b = new Bitmap(path)) return new Bitmap(b); }
}
public static class Program
{
    // BoardMaker <photosDir> <outDir> [creature...]
    public static int Main(string[] args)
    {
        string photos = args[0], outDir = args[1];
        var creatures = args.Length > 2 ? args.Skip(2).ToArray() : new[] { "Greyling", "Greydwarf", "Greydwarf_Shaman", "Greydwarf_Elite" };
        var nice = new Dictionary<string, string> { { "Greyling", "Greyling" }, { "Greydwarf", "Greydwarf" }, { "Greydwarf_Shaman", "Greydwarf Shaman" }, { "Greydwarf_Elite", "Greydwarf Brute" } };
        string[] views = { "front", "threequarter", "side", "back" }, heads = { "head_front", "head_threequarter" };
        Directory.CreateDirectory(outDir);
        var lineNames = new List<string>(); var lineFronts = new List<Bitmap>(); var lineHeights = new List<double>();

        foreach (var c in creatures)
        {
            var src = Path.Combine(photos, c);
            if (!Directory.Exists(src)) { Console.Error.WriteLine("no renders for " + c); continue; }
            var dst = Path.Combine(outDir, c);
            Directory.CreateDirectory(dst);
            var info = File.ReadAllLines(Path.Combine(src, "info.txt")).Where(l => l.Contains('='))
                .ToDictionary(l => l.Substring(0, l.IndexOf('=')), l => l.Substring(l.IndexOf('=') + 1));

            var v = views.Select(n => { var bm = RefBoards.Matte(Path.Combine(src, n + ".png")); bm.Save(Path.Combine(dst, n + ".png"), ImageFormat.Png); return bm; }).ToArray();
            var h = heads.Select(n => { var bm = RefBoards.Matte(Path.Combine(src, n + ".png")); bm.Save(Path.Combine(dst, n + ".png"), ImageFormat.Png); return bm; }).ToArray();
            foreach (var t in Directory.GetFiles(src, "texture_*.png")) File.Copy(t, Path.Combine(dst, Path.GetFileName(t)), true);
            var texFile = Directory.GetFiles(src, "texture_*.png").FirstOrDefault(f => Path.GetFileName(f).ToLower().Contains("grey"));
            Bitmap tex = texFile != null ? RefBoards.Load(texFile) : null;
            var texLabel = tex != null ? "albedo texture (" + tex.Width + "x" + tex.Height + ")" : "albedo texture";

            var pal = RefBoards.Palette(v, 8);
            var body = (info["meshes"].Split(new[] { ", " }, StringSplitOptions.None).FirstOrDefault(m => m.StartsWith("Cube:")) ?? "").Replace("Cube:", "body ");
            var sub = "Valheim 1.0.20  |  height " + info["height_m"] + " m  |  " + body + "  |  materials: " + info["materials"];
            var foot = "Rendered in-game with the game's own shaders: orthographic, flat studio lighting, idle pose, no equipment. Greenlings mod reference sheet.";
            var board = Path.Combine(outDir, c + "_board.png");
            RefBoards.Board(nice[c] + " - reference sheet", sub, new[] { "front", "three-quarter", "side", "back" }, v, h,
                new[] { "head - front", "head - three-quarter" }, tex, texLabel, pal, foot, board);
            Console.WriteLine("board: " + board);

            lineNames.Add(nice[c]); lineFronts.Add(v[0]); lineHeights.Add(double.Parse(info["height_m"], System.Globalization.CultureInfo.InvariantCulture));
        }
        var lineup = Path.Combine(outDir, "lineup.png");
        RefBoards.Lineup(lineNames.ToArray(), lineFronts.ToArray(), lineHeights.ToArray(), lineup);
        Console.WriteLine("lineup: " + lineup);
        return 0;
    }
}
