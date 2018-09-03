﻿// "Therefore those skilled at the unorthodox
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

namespace HtmlRendererCore.PdfSharp.Adapters
{
    using System;

    using global::PdfSharp.Drawing;

    using HtmlRendererCore.Adapters;

    /// <summary>
    /// Adapter for WinForms brushes objects for core.
    /// </summary>
    internal sealed class BrushAdapter : RBrush
    {
        /// <summary>
        /// The actual PdfSharp brush instance.<br/>
        /// Should be <see cref="XBrush"/> but there is some issue inheriting from it =/
        /// </summary>
        private readonly Object _brush;

        /// <summary>
        /// Init.
        /// </summary>
        public BrushAdapter(Object brush)
        {
            this._brush = brush;
        }

        /// <summary>
        /// The actual WinForms brush instance.
        /// </summary>
        public Object Brush
        {
            get { return this._brush; }
        }

        public override void Dispose()
        { }
    }
}