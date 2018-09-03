// "Therefore those skilled at the unorthodox
// are infinite as heaven and earth,
// inexhaustible as the great rivers.
// When they come to an end,
// they begin again,
// like the days and months;
// they die and are reborn,
// like the four seasons."
// 
// - Sun Tsu,
// "The Art of War"

namespace HtmlRendererCore.Core.Handlers
{
    using System;
    using System.Collections.Generic;

    using HtmlRendererCore.Adapters;
    using HtmlRendererCore.Adapters.Entities;
    using HtmlRendererCore.Core.Utils;

    /// <summary>
    /// Utilities for fonts and fonts families handling.
    /// </summary>
    internal sealed class FontsHandler
    {
        #region Fields and Consts

        /// <summary>
        /// 
        /// </summary>
        private readonly RAdapter _adapter;

        /// <summary>
        /// Allow to map not installed fonts to different
        /// </summary>
        private readonly Dictionary<string, string> _fontsMapping = new Dictionary<string, string>(StringComparer.InvariantCultureIgnoreCase);

        /// <summary>
        /// collection of all installed and added font families to check if font exists
        /// </summary>
        private readonly Dictionary<string, RFontFamily> _existingFontFamilies = new Dictionary<string, RFontFamily>(StringComparer.InvariantCultureIgnoreCase);

        /// <summary>
        /// cache of all the font used not to create same font again and again
        /// </summary>
        private readonly Dictionary<string, Dictionary<double, Dictionary<RFontStyle, RFont>>> _fontsCache = new Dictionary<string, Dictionary<double, Dictionary<RFontStyle, RFont>>>(StringComparer.InvariantCultureIgnoreCase);

        #endregion


        /// <summary>
        /// Init.
        /// </summary>
        public FontsHandler(RAdapter adapter)
        {
            ArgChecker.AssertArgNotNull(adapter, "global");

            this._adapter = adapter;
        }

        /// <summary>
        /// Check if the given font family exists by name
        /// </summary>
        /// <param name="family">the font to check</param>
        /// <returns>true - font exists by given family name, false - otherwise</returns>
        public bool IsFontExists(string family)
        {
            bool exists = this._existingFontFamilies.ContainsKey(family);
            if (!exists)
            {
                string mappedFamily;
                if (this._fontsMapping.TryGetValue(family, out mappedFamily))
                {
                    exists = this._existingFontFamilies.ContainsKey(mappedFamily);
                }
            }
            return exists;
        }

        /// <summary>
        /// Adds a font family to be used.
        /// </summary>
        /// <param name="fontFamily">The font family to add.</param>
        public void AddFontFamily(RFontFamily fontFamily)
        {
            ArgChecker.AssertArgNotNull(fontFamily, "family");

            this._existingFontFamilies[fontFamily.Name] = fontFamily;
        }

        /// <summary>
        /// Adds a font mapping from <paramref name="fromFamily"/> to <paramref name="toFamily"/> iff the <paramref name="fromFamily"/> is not found.<br/>
        /// When the <paramref name="fromFamily"/> font is used in rendered html and is not found in existing 
        /// fonts (installed or added) it will be replaced by <paramref name="toFamily"/>.<br/>
        /// </summary>
        /// <param name="fromFamily">the font family to replace</param>
        /// <param name="toFamily">the font family to replace with</param>
        public void AddFontFamilyMapping(string fromFamily, string toFamily)
        {
            ArgChecker.AssertArgNotNullOrEmpty(fromFamily, "fromFamily");
            ArgChecker.AssertArgNotNullOrEmpty(toFamily, "toFamily");

            this._fontsMapping[fromFamily] = toFamily;
        }

        /// <summary>
        /// Get cached font instance for the given font properties.<br/>
        /// Improve performance not to create same font multiple times.
        /// </summary>
        /// <returns>cached font instance</returns>
        public RFont GetCachedFont(string family, double size, RFontStyle style)
        {
            var font = this.TryGetFont(family, size, style);
            if (font == null)
            {
                if (!this._existingFontFamilies.ContainsKey(family))
                {
                    string mappedFamily;
                    if (this._fontsMapping.TryGetValue(family, out mappedFamily))
                    {
                        font = this.TryGetFont(mappedFamily, size, style);
                        if (font == null)
                        {
                            font = this.CreateFont(mappedFamily, size, style);
                            this._fontsCache[mappedFamily][size][style] = font;
                        }
                    }
                }

                if (font == null)
                {
                    font = this.CreateFont(family, size, style);
                }

                this._fontsCache[family][size][style] = font;
            }
            return font;
        }


        #region Private methods

        /// <summary>
        /// Get cached font if it exists in cache or null if it is not.
        /// </summary>
        private RFont TryGetFont(string family, double size, RFontStyle style)
        {
            RFont font = null;
            if (this._fontsCache.ContainsKey(family))
            {
                var a = this._fontsCache[family];
                if (a.ContainsKey(size))
                {
                    var b = a[size];
                    if (b.ContainsKey(style))
                    {
                        font = b[style];
                    }
                }
                else
                {
                    this._fontsCache[family][size] = new Dictionary<RFontStyle, RFont>();
                }
            }
            else
            {
                this._fontsCache[family] = new Dictionary<double, Dictionary<RFontStyle, RFont>>();
                this._fontsCache[family][size] = new Dictionary<RFontStyle, RFont>();
            }
            return font;
        }

        /// <summary>
        // create font (try using existing font family to support custom fonts)
        /// </summary>
        private RFont CreateFont(string family, double size, RFontStyle style)
        {
            RFontFamily fontFamily;
            try
            {
                return this._existingFontFamilies.TryGetValue(family, out fontFamily)
                    ? this._adapter.CreateFont(fontFamily, size, style)
                    : this._adapter.CreateFont(family, size, style);
            }
            catch
            {
                // handle possibility of no requested style exists for the font, use regular then
                return this._existingFontFamilies.TryGetValue(family, out fontFamily)
                    ? this._adapter.CreateFont(fontFamily, size, RFontStyle.Regular)
                    : this._adapter.CreateFont(family, size, RFontStyle.Regular);
            }
        }

        #endregion
    }
}