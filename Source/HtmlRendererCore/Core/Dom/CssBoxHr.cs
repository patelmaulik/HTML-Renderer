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

namespace HtmlRendererCore.Core.Dom
{
    using HtmlRendererCore.Adapters;
    using HtmlRendererCore.Adapters.Entities;
    using HtmlRendererCore.Core.Handlers;
    using HtmlRendererCore.Core.Parse;
    using HtmlRendererCore.Core.Utils;

    /// <summary>
    /// CSS box for hr element.
    /// </summary>
    internal sealed class CssBoxHr : CssBox
    {
        /// <summary>
        /// Init.
        /// </summary>
        /// <param name="parent">the parent box of this box</param>
        /// <param name="tag">the html tag data of this box</param>
        public CssBoxHr(CssBox parent, HtmlTag tag)
            : base(parent, tag)
        {
            this.Display = CssConstants.Block;
        }

        /// <summary>
        /// Measures the bounds of box and children, recursively.<br/>
        /// Performs layout of the DOM structure creating lines by set bounds restrictions.
        /// </summary>
        /// <param name="g">Device context to use</param>
        protected override void PerformLayoutImp(RGraphics g)
        {
            if (this.Display == CssConstants.None)
                return;

            this.RectanglesReset();

            var prevSibling = DomUtils.GetPreviousSibling(this);
            double left = this.ContainingBlock.Location.X + this.ContainingBlock.ActualPaddingLeft + this.ActualMarginLeft + this.ContainingBlock.ActualBorderLeftWidth;
            double top = (prevSibling == null && this.ParentBox != null ? this.ParentBox.ClientTop : this.ParentBox == null ? this.Location.Y : 0) + this.MarginTopCollapse(prevSibling) + (prevSibling != null ? prevSibling.ActualBottom + prevSibling.ActualBorderBottomWidth : 0);
            this.Location = new RPoint(left, top);
            this.ActualBottom = top;

            //width at 100% (or auto)
            double minwidth = this.GetMinimumWidth();
            double width = this.ContainingBlock.Size.Width
                           - this.ContainingBlock.ActualPaddingLeft - this.ContainingBlock.ActualPaddingRight
                           - this.ContainingBlock.ActualBorderLeftWidth - this.ContainingBlock.ActualBorderRightWidth
                           - this.ActualMarginLeft - this.ActualMarginRight - this.ActualBorderLeftWidth - this.ActualBorderRightWidth;

            //Check width if not auto
            if (this.Width != CssConstants.Auto && !string.IsNullOrEmpty(this.Width))
            {
                width = CssValueParser.ParseLength(this.Width, width, this);
            }

            if (width < minwidth || width >= 9999)
                width = minwidth;

            double height = this.ActualHeight;
            if (height < 1)
            {
                height = this.Size.Height + this.ActualBorderTopWidth + this.ActualBorderBottomWidth;
            }
            if (height < 1)
            {
                height = 2;
            }
            if (height <= 2 && this.ActualBorderTopWidth < 1 && this.ActualBorderBottomWidth < 1)
            {
                this.BorderTopStyle = this.BorderBottomStyle = CssConstants.Solid;
                this.BorderTopWidth = "1px";
                this.BorderBottomWidth = "1px";
            }

            this.Size = new RSize(width, height);

            this.ActualBottom = this.Location.Y + this.ActualPaddingTop + this.ActualPaddingBottom + height;
        }

        /// <summary>
        /// Paints the fragment
        /// </summary>
        /// <param name="g">the device to draw to</param>
        protected override void PaintImp(RGraphics g)
        {
            var offset = (this.HtmlContainer != null && !this.IsFixed) ? this.HtmlContainer.ScrollOffset : RPoint.Empty;
            var rect = new RRect(this.Bounds.X + offset.X, this.Bounds.Y + offset.Y, this.Bounds.Width, this.Bounds.Height);

            if (rect.Height > 2 && RenderUtils.IsColorVisible(this.ActualBackgroundColor))
            {
                g.DrawRectangle(g.GetSolidBrush(this.ActualBackgroundColor), rect.X, rect.Y, rect.Width, rect.Height);
            }

            var b1 = g.GetSolidBrush(this.ActualBorderTopColor);
            BordersDrawHandler.DrawBorder(Border.Top, g, this, b1, rect);

            if (rect.Height > 1)
            {
                var b2 = g.GetSolidBrush(this.ActualBorderLeftColor);
                BordersDrawHandler.DrawBorder(Border.Left, g, this, b2, rect);

                var b3 = g.GetSolidBrush(this.ActualBorderRightColor);
                BordersDrawHandler.DrawBorder(Border.Right, g, this, b3, rect);

                var b4 = g.GetSolidBrush(this.ActualBorderBottomColor);
                BordersDrawHandler.DrawBorder(Border.Bottom, g, this, b4, rect);
            }
        }
    }
}