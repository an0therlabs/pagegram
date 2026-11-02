using System;
using Pagegram.Models;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Documents;
using Windows.UI.Xaml.Media;

namespace Pagegram.Views
{
    /// <summary>
    /// Прикреплённое свойство для TextBlock: рисует текст сообщения, а @username, ссылки и
    /// почту — синими кликабельными Hyperlink. Клик уходит в событие LinkClicked (его слушает ChatPage).
    /// </summary>
    public static class RichText
    {
        public static event Action<LinkSpan> LinkClicked;

        private static SolidColorBrush _linkBrush;
        private static Brush LinkBrush => _linkBrush ?? (_linkBrush = new SolidColorBrush(Color.FromArgb(255, 0x8C, 0xC8, 0xFF)));

        public static readonly DependencyProperty ContentProperty = DependencyProperty.RegisterAttached(
            "Content", typeof(RichContent), typeof(RichText), new PropertyMetadata(null, OnContentChanged));

        public static RichContent GetContent(DependencyObject d) { return (RichContent)d.GetValue(ContentProperty); }
        public static void SetContent(DependencyObject d, RichContent v) { d.SetValue(ContentProperty, v); }

        private static void OnContentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var tb = d as TextBlock;
            if (tb == null) return;
            tb.Inlines.Clear();
            var c = e.NewValue as RichContent;
            if (c == null || c.Text.Length == 0) return;

            if (c.Links == null)
            {
                tb.Inlines.Add(new Run { Text = c.Text });
                return;
            }

            int pos = 0;
            foreach (var link in c.Links)
            {
                if (link.Offset < pos || link.Offset + link.Length > c.Text.Length) continue; // перекрытия/мусор
                if (link.Offset > pos) tb.Inlines.Add(new Run { Text = c.Text.Substring(pos, link.Offset - pos) });

                var h = new Hyperlink { Foreground = LinkBrush, UnderlineStyle = UnderlineStyle.None };
                h.Inlines.Add(new Run { Text = c.Text.Substring(link.Offset, link.Length) });
                var captured = link;
                h.Click += (s, a) => LinkClicked?.Invoke(captured);
                tb.Inlines.Add(h);
                pos = link.Offset + link.Length;
            }
            if (pos < c.Text.Length) tb.Inlines.Add(new Run { Text = c.Text.Substring(pos) });
        }
    }
}
