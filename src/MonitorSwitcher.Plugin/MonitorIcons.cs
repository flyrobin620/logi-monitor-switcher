using System;
using System.Globalization;
using System.Text;

namespace Loupedeck.MonitorSwitcherPlugin
{
    internal static class MonitorIcons
    {
        private const string Start = "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"100\" height=\"100\" viewBox=\"0 0 100 100\">";
        private static readonly string[] Digits =
        {
            "M8 0C1 0 1 5 1 12S1 24 8 24S15 19 15 12S15 0 8 0Z",
            "M2 5L9 0V24M2 24H16",
            "M1 5Q1 0 8 0Q15 0 15 6Q15 10 8 15L1 24H15",
            "M1 1H15L7 11Q16 10 15 18Q15 26 1 23",
            "M12 24V0L0 17H17",
            "M15 0H2V11Q16 7 15 18Q15 26 1 23",
            "M14 1Q1 -3 1 13V18Q1 24 8 24Q15 24 15 17Q15 10 8 10Q1 10 1 16",
            "M1 0H16L5 24",
            "M8 12C-2 12 -2 0 8 0S18 12 8 12C-2 12 -2 24 8 24S18 12 8 12",
            "M2 23Q15 27 15 11V6Q15 0 8 0Q1 0 1 7Q1 14 8 14Q15 14 15 8"
        };

        internal const string ParentSvg =
            "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"100\" height=\"100\" viewBox=\"0 0 100 100\">" +
            "<rect x=\"14\" y=\"18\" width=\"57\" height=\"39\" rx=\"5\" fill=\"#11263a\" stroke=\"#ffffff\" stroke-width=\"5\"/>" +
            "<rect x=\"30\" y=\"32\" width=\"57\" height=\"39\" rx=\"5\" fill=\"#11263a\" stroke=\"#69c9ff\" stroke-width=\"5\"/>" +
            "<path d=\"M59 73v8m-13 0h26M19 78h18l-6 -6m6 6l-6 6\" fill=\"none\" stroke=\"#ffffff\" stroke-width=\"5\" stroke-linecap=\"round\" stroke-linejoin=\"round\"/></svg>";

        internal static string Render(int? index, bool isPrimary)
        {
            if (index.HasValue && index.Value < 1)
                throw new ArgumentOutOfRangeException(nameof(index));

            var color = !index.HasValue ? "#ff9292" : isPrimary ? "#63edac" : "#ffffff";
            var svg = new StringBuilder(Start);
            svg.Append("<rect x=\"6\" y=\"18\" width=\"88\" height=\"58\" rx=\"5\" fill=\"#11263a\" stroke=\"")
                .Append(color).Append("\" stroke-width=\"5\"/>");
            svg.Append("<path d=\"M50 78v12M32 91h36\" fill=\"none\" stroke=\"")
                .Append(color).Append("\" stroke-width=\"5\" stroke-linecap=\"round\"/>");

            if (!index.HasValue)
                svg.Append("<path d=\"M40 38l20 20m0 -20l-20 20\" stroke=\"#ff9292\" stroke-width=\"6\" stroke-linecap=\"round\"/>");
            else
            {
                var digits = index.Value.ToString(CultureInfo.InvariantCulture);
                var scale = Math.Min(1.7, 68.0 / (digits.Length * 22 - 6));
                var left = 50 - (digits.Length * 22 - 6) * scale / 2;
                svg.AppendFormat(CultureInfo.InvariantCulture,
                    "<g transform=\"translate({0:0.###} {1:0.###}) scale({2:0.###})\" fill=\"none\" stroke=\"{3}\" stroke-width=\"3.5\" stroke-linecap=\"round\" stroke-linejoin=\"round\">",
                    left, 47.5 - 12 * scale, scale, color);
                for (var i = 0; i < digits.Length; i++)
                    svg.AppendFormat(CultureInfo.InvariantCulture,
                        "<path transform=\"translate({0} 0)\" d=\"{1}\"/>", i * 22, Digits[digits[i] - '0']);
                svg.Append("</g>");
            }
            if (isPrimary && index.HasValue)
                svg.Append("<path d=\"M79 7l4 8 9 1-6.5 6 1.5 9-8-4-8 4 1.5-9-6.5-6 9-1z\" fill=\"#ffd15b\" stroke=\"#11263a\" stroke-width=\"2.5\"/>");
            return svg.Append("</svg>").ToString();
        }
    }
}
