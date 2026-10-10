using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

static class Shapes {
    internal static GraphicsPath Round(RectangleF r, float radius) {
        float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        var path = new GraphicsPath();
        path.AddArc(r.Left, r.Top, d, d, 180, 90); path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90); path.CloseFigure(); return path;
    }
    internal static Color Blend(Color a, Color b, double t) {
        return Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
    }
}
sealed class GlowButton : Button {
    readonly Timer animation = new Timer { Interval = 16 };
    bool hovered, pressed;
    double glow;
    internal Color GlowColor = Color.FromArgb(75, 224, 199);
    internal GlowButton() {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        FlatStyle = FlatStyle.Flat; Cursor = Cursors.Hand;
        animation.Tick += delegate { double target = Enabled && (hovered || Focused) ? 1 : 0; glow += Math.Sign(target - glow) * Math.Min(.12, Math.Abs(target - glow)); Invalidate(); if (Math.Abs(target - glow) < .001) animation.Stop(); };
    }
    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hovered = true; animation.Start(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hovered = false; pressed = false; animation.Start(); }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); animation.Start(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); pressed = false; animation.Start(); }
    protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); pressed = e.Button == MouseButtons.Left; Invalidate(); }
    protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); pressed = false; Invalidate(); }
    protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); animation.Start(); Invalidate(); }
    protected override void OnPaint(PaintEventArgs e) {
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent == null ? BackColor : Parent.BackColor);
        var bounds = new RectangleF(4, 4, Math.Max(1, Width - 9), Math.Max(1, Height - 9));
        using (var shape = Shapes.Round(bounds, 10)) {
            if (Enabled && glow > 0) {
                foreach (float width in new[] { 8f, 5f, 3f }) using (var halo = new Pen(Color.FromArgb((int)(glow * (width == 8 ? 14 : width == 5 ? 23 : 40)), GlowColor), width)) g.DrawPath(halo, shape);
            }
            Color fill = Enabled ? Shapes.Blend(BackColor, GlowColor, pressed ? .18 : glow * .09) : Shapes.Blend(BackColor, Parent == null ? BackColor : Parent.BackColor, .6);
            using (var brush = new SolidBrush(fill)) g.FillPath(brush, shape);
            if (FlatAppearance.BorderSize > 0 || !Enabled) using (var pen = new Pen(Enabled ? Shapes.Blend(FlatAppearance.BorderColor, GlowColor, glow * .8) : FlatAppearance.BorderColor, 1)) g.DrawPath(pen, shape);
            Color text = Enabled ? ForeColor : (BackColor.GetBrightness() < .5 ? Color.FromArgb(111, 130, 151) : Color.FromArgb(112, 126, 139));
            TextRenderer.DrawText(g, Text, Font, Rectangle.Round(bounds), text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }
    protected override void Dispose(bool disposing) { if (disposing) animation.Dispose(); base.Dispose(disposing); }
}
sealed class InputShell : Panel {
    readonly TextBox editor;
    internal Color BorderColor, Accent;
    internal InputShell(TextBox input) {
        editor = input; Dock = DockStyle.Fill; Margin = new Padding(0, 2, 4, 2); Padding = new Padding(9, 7, 9, 6);
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        input.BorderStyle = BorderStyle.None; input.Dock = DockStyle.Fill; input.Margin = Padding.Empty; Controls.Add(input);
        if (input.Multiline) { input.ScrollBars = ScrollBars.None; Controls.Add(new GrayScroll(input) { Dock = DockStyle.Right, Width = 15 }); }
        input.Enter += delegate { Invalidate(); }; input.Leave += delegate { Invalidate(); };
    }
    protected override void OnPaintBackground(PaintEventArgs e) { e.Graphics.Clear(Parent == null ? BackColor : Parent.BackColor); }
    protected override void OnPaint(PaintEventArgs e) {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using (var path = Shapes.Round(new RectangleF(.5f, .5f, Math.Max(1, Width - 1), Math.Max(1, Height - 1)), 7))
        using (var brush = new SolidBrush(BackColor)) using (var pen = new Pen(editor.Focused ? Accent : BorderColor)) { e.Graphics.FillPath(brush, path); e.Graphics.DrawPath(pen, path); }
    }
}
sealed class GrayCheck : CheckBox {
    internal Color Accent, Border;
    internal GrayCheck() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true); Cursor = Cursors.Hand; }
    protected override void OnPaint(PaintEventArgs e) {
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.Clear(BackColor);
        float top = (Height - 15) / 2f;
        using (var path = Shapes.Round(new RectangleF(.5f, top, 14, 14), 3))
        using (var brush = new SolidBrush(Checked ? Accent : Shapes.Blend(BackColor, Border, .35)))
        using (var pen = new Pen(Checked ? Accent : Border)) { g.FillPath(brush, path); g.DrawPath(pen, path); }
        if (Checked) using (var pen = new Pen(Color.FromArgb(11, 38, 39), 2)) g.DrawLines(pen, new[] { new PointF(3.5f, top + 7), new PointF(6, top + 10), new PointF(11, top + 4) });
        var text = new Rectangle(21, 0, Math.Max(0, Width - 21), Height);
        TextRenderer.DrawText(g, Text, Font, text, Enabled ? ForeColor : Color.Gray, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
        if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(g, text, ForeColor, BackColor);
    }
}
sealed class GrayScroll : Control {
    readonly TextBox editor;
    bool dragging, hovered; float dragOffset;
    internal Color Thumb, Hover;
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr SendMessage(IntPtr h, int message, IntPtr w, IntPtr l);
    internal GrayScroll(TextBox text) {
        editor = text; SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        Cursor = Cursors.Hand; TabStop = false;
        editor.TextChanged += delegate { Invalidate(); }; editor.KeyUp += delegate { Invalidate(); }; editor.MouseWheel += delegate { BeginInvoke(new Action(Invalidate)); };
        editor.MouseUp += delegate { Invalidate(); }; editor.Resize += delegate { Invalidate(); };
    }
    int TopLine { get { return editor.IsHandleCreated ? editor.GetLineFromCharIndex(editor.GetCharIndexFromPosition(Point.Empty)) : 0; } }
    int VisibleLines { get { return Math.Max(1, editor.ClientSize.Height / editor.Font.Height); } }
    int TotalLines { get { return editor.IsHandleCreated ? Math.Max(1, editor.GetLineFromCharIndex(editor.TextLength) + 1) : 1; } }
    int MaxLine { get { return Math.Max(0, TotalLines - VisibleLines); } }
    RectangleF ThumbBounds {
        get { float track = Math.Max(1, Height - 4), height = Math.Min(track, Math.Max(24, track * VisibleLines / TotalLines)); return new RectangleF(4, 2 + (track - height) * (MaxLine == 0 ? 0 : Math.Min(1, (float)TopLine / MaxLine)), Math.Max(2, Width - 7), height); }
    }
    void ScrollTo(int value) { value = Math.Max(0, Math.Min(MaxLine, value)); SendMessage(editor.Handle, 0xB6, IntPtr.Zero, new IntPtr(value - TopLine)); Invalidate(); }
    protected override void OnPaint(PaintEventArgs e) {
        e.Graphics.Clear(BackColor); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        if (MaxLine > 0) using (var path = Shapes.Round(ThumbBounds, 3)) using (var brush = new SolidBrush(hovered || dragging ? Hover : Thumb)) e.Graphics.FillPath(brush, path);
    }
    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hovered = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hovered = false; Invalidate(); }
    protected override void OnMouseDown(MouseEventArgs e) {
        base.OnMouseDown(e); if (e.Button != MouseButtons.Left || MaxLine == 0) return;
        var thumb = ThumbBounds;
        if (thumb.Contains(e.Location)) { dragging = true; dragOffset = e.Y - thumb.Y; Capture = true; }
        else ScrollTo(TopLine + (e.Y < thumb.Y ? -VisibleLines : VisibleLines));
    }
    protected override void OnMouseMove(MouseEventArgs e) {
        base.OnMouseMove(e); if (!dragging) return;
        float range = Height - 4 - ThumbBounds.Height; if (range > 0) ScrollTo((int)Math.Round((e.Y - dragOffset - 2) * MaxLine / range));
    }
    protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); dragging = false; Capture = false; Invalidate(); }
    protected override void OnMouseWheel(MouseEventArgs e) { base.OnMouseWheel(e); ScrollTo(TopLine - Math.Sign(e.Delta) * 3); }
}
sealed class FlatProgress : ProgressBar {
    internal Color Accent, Track;
    internal FlatProgress() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true); }
    protected override void WndProc(ref Message m) { base.WndProc(ref m); if (m.Msg == 0x402) Invalidate(); }
    protected override void OnPaint(PaintEventArgs e) {
        e.Graphics.Clear(Track);
        int width = Maximum > Minimum ? (int)((double)(Value - Minimum) * Width / (Maximum - Minimum)) : 0;
        if (width > 0) using (var brush = new SolidBrush(Accent)) e.Graphics.FillRectangle(brush, 0, 0, width, Height);
    }
}
