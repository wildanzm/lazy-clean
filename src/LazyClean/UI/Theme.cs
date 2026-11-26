using System.Drawing;

namespace Cleaner
{
    // Windows utility direction: light neutral panels, one cold Windows blue accent reserved for the
    // primary action, the selected filter and the active tab. Everything else is grey on white.
    //
    // Every text pairing below was computed, not eyeballed (WCAG 2.x relative luminance):
    //   Body  on Canvas  14.35:1    Muted on Card   5.82:1
    //   Body  on Card    15.80:1    Muted on Inset  5.47:1
    //   Quiet on Card     5.15:1    Quiet on Inset  4.84:1    Quiet on Canvas 4.68:1
    //   White on Accent   6.01:1    Accent on Card  6.01:1
    // Control borders must clear 3:1 (WCAG 1.4.11), which is what Border is for:
    //   Border on Card 3.45:1       Border on Canvas 3.13:1
    // Line stays a decorative hairline (1.31:1) and is only ever a 1px separator, never a
    // component edge or anything carrying meaning.
    public static class Theme
    {
        public static readonly Color Canvas = Color.FromArgb(0xF3, 0xF4, 0xF6);
        public static readonly Color Card = Color.White;
        public static readonly Color Inset = Color.FromArgb(0xF7, 0xF8, 0xFA);
        public static readonly Color Line = Color.FromArgb(0xDD, 0xE1, 0xE6);
        public static readonly Color Body = Color.FromArgb(0x1F, 0x23, 0x28);
        public static readonly Color Muted = Color.FromArgb(0x5E, 0x66, 0x70);
        public static readonly Color Quiet = Color.FromArgb(0x66, 0x6E, 0x7B);
        public static readonly Color Border = Color.FromArgb(0x82, 0x8B, 0x98);
        public static readonly Color Accent = Color.FromArgb(0x1F, 0x5F, 0xC4);
        public static readonly Color AccentDeep = Color.FromArgb(0x17, 0x49, 0x99);
        public static readonly Color AccentSoft = Color.FromArgb(0xE4, 0xED, 0xFB);

        public const string Ui = "Segoe UI";
        public const string Mono = "Consolas";

        public static Font UiFont(float size, FontStyle style)
        {
            return new Font(Ui, size, style);
        }

        public static Font MonoFont(float size)
        {
            return new Font(Mono, size, FontStyle.Regular);
        }
    }
}
