using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

// A flat page switcher keeps native WinForms controls while avoiding the
// system TabControl's bright strip in dark mode.
sealed class TabSheet : Panel { internal TabSheet(string title) { Text = title; } }
sealed class TabDeck : Panel {
    readonly FlowLayoutPanel navigation = new FlowLayoutPanel();
    readonly List<Button> buttons = new List<Button>();
    internal readonly Pages TabSheets;
    int selected;
    internal Color Accent, Surface;
    internal TabDeck() {
        TabSheets = new Pages(this); navigation.Dock = DockStyle.Top; navigation.Height = 44;
        navigation.Padding = new Padding(0, 0, 0, 6); Controls.Add(navigation);
    }
    internal int SelectedIndex {
        get { return selected; }
        set { if (value < 0 || value >= TabSheets.Count) throw new ArgumentOutOfRangeException(); selected = value; UpdatePages(); }
    }
    internal void RefreshTheme(Color accent, Color surface) { Accent = accent; Surface = surface; UpdatePages(); }
    void UpdatePages() {
        for (int i = 0; i < TabSheets.Count; i++) {
            TabSheets[i].Visible = i == selected;
            buttons[i].BackColor = i == selected ? Accent : Surface;
            buttons[i].ForeColor = i == selected ? Color.FromArgb(10, 32, 34) : ForeColor;
        }
    }
    protected override void OnResize(EventArgs e) {
        base.OnResize(e); if (TabSheets != null) foreach (var page in TabSheets) page.SetBounds(0, 44, ClientSize.Width, Math.Max(0, ClientSize.Height - 44));
    }
    internal sealed class Pages : List<TabSheet> {
        readonly TabDeck deck;
        internal Pages(TabDeck owner) { deck = owner; }
        internal new void AddRange(IEnumerable<TabSheet> pages) {
            foreach (var page in pages) {
                int index = Count; Add(page);
                page.Dock = DockStyle.None; deck.Controls.Add(page); page.SetBounds(0, 44, deck.ClientSize.Width, Math.Max(0, deck.ClientSize.Height - 44));
                var button = new GlowButton { Text = page.Text, Size = new Size(110, 36), Margin = new Padding(0, 0, 6, 0), FlatStyle = FlatStyle.Flat };
                button.Click += delegate { deck.SelectedIndex = index; }; deck.buttons.Add(button); deck.navigation.Controls.Add(button);
            }
            deck.navigation.BringToFront(); deck.UpdatePages();
        }
    }
}
