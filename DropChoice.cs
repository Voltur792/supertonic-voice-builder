using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

// Fully painted control: no native ComboBox frame or arrow can flash white.
sealed class StyledCombo : Control {
    internal readonly List<object> Items = new List<object>();
    internal ComboBoxStyle DropDownStyle { get; set; }
    internal Color Accent, Border;
    int selected = -1;
    bool hovered;
    ToolStripDropDown popup;
    internal StyledCombo() {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Selectable, true);
        Height = 32; MinimumSize = new Size(80, 32); Cursor = Cursors.Hand; TabStop = true; AccessibleRole = AccessibleRole.ComboBox;
    }
    internal int SelectedIndex {
        get { return selected; }
        set { if (value < -1 || value >= Items.Count) throw new ArgumentOutOfRangeException("SelectedIndex"); selected = value; Text = value < 0 ? "" : Convert.ToString(Items[value]); Invalidate(); }
    }
    protected override void OnPaint(PaintEventArgs e) {
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent == null ? BackColor : Parent.BackColor);
        using (var path = Shapes.Round(new RectangleF(.5f, .5f, Math.Max(1, Width - 1), Math.Max(1, Height - 1)), 7))
        using (var brush = new SolidBrush(BackColor)) using (var pen = new Pen(Enabled && (hovered || Focused) ? Accent : Border)) { g.FillPath(brush, path); g.DrawPath(pen, path); }
        var ink = Enabled ? ForeColor : Color.FromArgb(110, 128, 149);
        TextRenderer.DrawText(g, Text, Font, new Rectangle(10, 1, Math.Max(1, Width - 35), Height - 2), ink, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        using (var b = new SolidBrush(ink)) g.FillPolygon(b, new[] { new Point(Width - 19, Height / 2 - 2), new Point(Width - 11, Height / 2 - 2), new Point(Width - 15, Height / 2 + 2) });
    }
    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hovered = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hovered = false; Invalidate(); }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
    protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); if (!Enabled && popup != null) popup.Close(); Invalidate(); }
    protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if (e.Button == MouseButtons.Left && Enabled) { Focus(); OpenMenu(); } }
    protected override bool IsInputKey(Keys keyData) { return keyData == Keys.Up || keyData == Keys.Down || base.IsInputKey(keyData); }
    protected override void OnKeyDown(KeyEventArgs e) {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter || e.KeyCode == Keys.F4 || (e.Alt && e.KeyCode == Keys.Down)) { OpenMenu(); e.Handled = true; }
        else if (e.KeyCode == Keys.Down && selected < Items.Count - 1) { SelectedIndex++; e.Handled = true; }
        else if (e.KeyCode == Keys.Up && selected > 0) { SelectedIndex--; e.Handled = true; }
    }
    void OpenMenu() {
        if (Items.Count == 0) return;
        if (popup != null && popup.Visible) { popup.Close(); return; }
        if (popup != null) popup.Dispose();
        var menu = new ChoiceMenu(this) { Size = new Size(Width - 2, Items.Count * 36 + 8), BackColor = BackColor, ForeColor = ForeColor, Font = Font };
        var host = new ToolStripControlHost(menu) { AutoSize = false, Size = menu.Size, Margin = Padding.Empty, Padding = Padding.Empty };
        popup = new ToolStripDropDown { Padding = new Padding(1), BackColor = BackColor, AutoClose = true, Renderer = new PopupRenderer(BackColor, Border) };
        popup.Items.Add(host); popup.Show(this, new Point(0, Height + 4)); menu.Focus();
    }
    protected override void Dispose(bool disposing) { if (disposing && popup != null) popup.Dispose(); base.Dispose(disposing); }
    sealed class PopupRenderer : ToolStripRenderer {
        readonly Color background, border;
        internal PopupRenderer(Color bg, Color line) { background = bg; border = line; }
        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e) { e.Graphics.Clear(background); }
        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e) { using (var p = new Pen(border)) e.Graphics.DrawRectangle(p, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1); }
    }
    sealed class ChoiceMenu : Control {
        readonly StyledCombo owner; int hovered;
        internal ChoiceMenu(StyledCombo choice) {
            owner = choice; hovered = Math.Max(0, choice.SelectedIndex); Cursor = Cursors.Hand;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Selectable, true); TabStop = true;
        }
        protected override void OnPaint(PaintEventArgs e) {
            e.Graphics.Clear(BackColor); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            for (int i = 0; i < owner.Items.Count; i++) {
                var row = new Rectangle(4, 4 + i * 36, Width - 8, 34);
                if (i == hovered) using (var shape = Shapes.Round(row, 6)) using (var brush = new SolidBrush(Shapes.Blend(BackColor, owner.Accent, .18))) e.Graphics.FillPath(brush, shape);
                TextRenderer.DrawText(e.Graphics, Convert.ToString(owner.Items[i]), Font, new Rectangle(11, row.Y, Width - 34, row.Height), ForeColor, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                if (i == owner.SelectedIndex) using (var p = new Pen(owner.Accent, 2)) e.Graphics.DrawLines(p, new[] { new Point(Width - 22, row.Y + 16), new Point(Width - 18, row.Y + 20), new Point(Width - 12, row.Y + 12) });
            }
        }
        protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); hovered = Math.Max(0, Math.Min(owner.Items.Count - 1, (e.Y - 4) / 36)); Invalidate(); }
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if (e.Button == MouseButtons.Left) Choose(Math.Max(0, Math.Min(owner.Items.Count - 1, (e.Y - 4) / 36))); }
        void Choose(int index) { owner.SelectedIndex = index; owner.popup.Close(); owner.Focus(); }
        protected override bool IsInputKey(Keys keyData) { return keyData == Keys.Up || keyData == Keys.Down || base.IsInputKey(keyData); }
        protected override void OnKeyDown(KeyEventArgs e) {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape) { owner.popup.Close(); owner.Focus(); }
            else if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) Choose(hovered);
            else if (e.KeyCode == Keys.Up) { hovered = Math.Max(0, hovered - 1); Invalidate(); }
            else if (e.KeyCode == Keys.Down) { hovered = Math.Min(owner.Items.Count - 1, hovered + 1); Invalidate(); }
            e.Handled = true;
        }
    }
}
