using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;

sealed class GrayNumber : Control {
    readonly TextBox editor = new TextBox { BorderStyle = BorderStyle.None };
    readonly Timer repeat = new Timer();
    decimal current, maximum = 100, minimum, increment = 1;
    int decimals, direction;
    bool hovered, updating;
    internal Color Spin, Arrow, Border, Accent;
    internal GrayNumber() {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        Height = 32; MinimumSize = new Size(64, 32); Controls.Add(editor); TabStop = false;
        editor.Leave += delegate { Commit(); FormatValue(); Invalidate(); }; editor.Enter += delegate { Invalidate(); };
        editor.KeyDown += delegate(object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Up) { UpButton(); e.Handled = true; } else if (e.KeyCode == Keys.Down) { DownButton(); e.Handled = true; } else if (e.KeyCode == Keys.Enter) { Commit(); FormatValue(); e.Handled = true; } };
        repeat.Tick += delegate { repeat.Interval = 80; if (direction > 0) UpButton(); else DownButton(); };
        FormatValue();
    }
    internal decimal Value { get { Commit(); return current; } set { current = Math.Max(minimum, Math.Min(maximum, value)); FormatValue(); } }
    internal decimal Maximum { get { return maximum; } set { maximum = value; Value = current; } }
    internal decimal Minimum { get { return minimum; } set { minimum = value; Value = current; } }
    internal decimal Increment { get { return increment; } set { if (value <= 0) throw new ArgumentOutOfRangeException(); increment = value; } }
    internal int DecimalPlaces { get { return decimals; } set { if (value < 0 || value > 10) throw new ArgumentOutOfRangeException(); decimals = value; FormatValue(); } }
    void Commit() {
        if (updating) return;
        decimal value; string text = editor.Text.Replace(".", CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator);
        if (decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out value)) current = Math.Max(minimum, Math.Min(maximum, value));
    }
    void FormatValue() { updating = true; editor.Text = current.ToString("F" + decimals, CultureInfo.CurrentCulture); updating = false; }
    internal void UpButton() { Value = Value + increment; }
    internal void DownButton() { Value = Value - increment; }
    internal void RefreshSpin() { editor.BackColor = BackColor; editor.ForeColor = Enabled ? ForeColor : Color.Gray; Invalidate(); }
    protected override void OnResize(EventArgs e) { base.OnResize(e); LayoutEditor(); }
    protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); if (editor != null) { editor.Font = Font; LayoutEditor(); } }
    void LayoutEditor() { if (editor != null) editor.SetBounds(8, Math.Max(1, (Height - editor.PreferredHeight) / 2), Math.Max(8, Width - 34), editor.PreferredHeight); }
    protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); repeat.Stop(); RefreshSpin(); }
    protected override void OnPaint(PaintEventArgs e) {
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.Clear(Parent == null ? BackColor : Parent.BackColor);
        using (var shape = Shapes.Round(new RectangleF(.5f, .5f, Math.Max(1, Width - 1), Math.Max(1, Height - 1)), 7)) {
            using (var brush = new SolidBrush(BackColor)) g.FillPath(brush, shape);
            var saved = g.Save(); g.SetClip(shape);
            using (var brush = new SolidBrush(hovered && Enabled ? Shapes.Blend(Spin, Accent, .15) : Spin)) g.FillRectangle(brush, Width - 24, 0, 24, Height);
            g.Restore(saved);
            using (var p = new Pen(editor.Focused ? Accent : Border)) g.DrawPath(p, shape);
        }
        using (var brush = new SolidBrush(Enabled ? Arrow : Color.Gray)) {
            int x = Width - 12; g.FillPolygon(brush, new[] { new Point(x - 3, Height / 4 + 2), new Point(x + 3, Height / 4 + 2), new Point(x, Height / 4 - 2) });
            g.FillPolygon(brush, new[] { new Point(x - 3, 3 * Height / 4 - 2), new Point(x + 3, 3 * Height / 4 - 2), new Point(x, 3 * Height / 4 + 2) });
        }
    }
    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hovered = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hovered = false; Invalidate(); }
    protected override void OnMouseDown(MouseEventArgs e) {
        base.OnMouseDown(e); if (e.Button != MouseButtons.Left || !Enabled) return;
        editor.Focus(); if (e.X < Width - 24) return;
        direction = e.Y < Height / 2 ? 1 : -1; if (direction > 0) UpButton(); else DownButton();
        Capture = true; repeat.Interval = 400; repeat.Start();
    }
    protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); repeat.Stop(); Capture = false; }
    protected override void OnMouseCaptureChanged(EventArgs e) { base.OnMouseCaptureChanged(e); if (!Capture) repeat.Stop(); }
    protected override void OnMouseWheel(MouseEventArgs e) { base.OnMouseWheel(e); if (editor.Focused) { if (e.Delta > 0) UpButton(); else DownButton(); } }
    protected override void Dispose(bool disposing) { if (disposing) repeat.Dispose(); base.Dispose(disposing); }
}
