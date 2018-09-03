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

namespace HtmlRendererCore.Core.Dom
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;

    using HtmlRendererCore.Adapters;
    using HtmlRendererCore.Adapters.Entities;
    using HtmlRendererCore.Core.Entities;
    using HtmlRendererCore.Core.Handlers;
    using HtmlRendererCore.Core.Parse;
    using HtmlRendererCore.Core.Utils;

    /// <summary>
    /// Represents a CSS Box of text or replaced elements.
    /// </summary>
    /// <remarks>
    /// The Box can contains other boxes, that's the way that the CSS Tree
    /// is composed.
    /// 
    /// To know more about boxes visit CSS spec:
    /// http://www.w3.org/TR/CSS21/box.html
    /// </remarks>
    internal class CssBox : CssBoxProperties, IDisposable
    {
        #region Fields and Consts

        /// <summary>
        /// the parent css box of this css box in the hierarchy
        /// </summary>
        private CssBox _parentBox;

        /// <summary>
        /// the root container for the hierarchy
        /// </summary>
        protected HtmlContainerInt _htmlContainer;

        /// <summary>
        /// the html tag that is associated with this css box, null if anonymous box
        /// </summary>
        private readonly HtmlTag _htmltag;

        private readonly List<CssRect> _boxWords = new List<CssRect>();
        private readonly List<CssBox> _boxes = new List<CssBox>();
        private readonly List<CssLineBox> _lineBoxes = new List<CssLineBox>();
        private readonly List<CssLineBox> _parentLineBoxes = new List<CssLineBox>();
        private readonly Dictionary<CssLineBox, RRect> _rectangles = new Dictionary<CssLineBox, RRect>();

        /// <summary>
        /// the inner text of the box
        /// </summary>
        private SubString _text;

        /// <summary>
        /// Do not use or alter this flag
        /// </summary>
        /// <remarks>
        /// Flag that indicates that CssTable algorithm already made fixes on it.
        /// </remarks>
        internal bool _tableFixed;

        protected bool _wordsSizeMeasured;
        private CssBox _listItemBox;
        private CssLineBox _firstHostingLineBox;
        private CssLineBox _lastHostingLineBox;

        /// <summary>
        /// handler for loading background image
        /// </summary>
        private ImageLoadHandler _imageLoadHandler;

        #endregion


        /// <summary>
        /// Init.
        /// </summary>
        /// <param name="parentBox">optional: the parent of this css box in html</param>
        /// <param name="tag">optional: the html tag associated with this css box</param>
        public CssBox(CssBox parentBox, HtmlTag tag)
        {
            if (parentBox != null)
            {
                this._parentBox = parentBox;
                this._parentBox.Boxes.Add(this);
            }
            this._htmltag = tag;
        }

        /// <summary>
        /// Gets the HtmlContainer of the Box.
        /// WARNING: May be null.
        /// </summary>
        public HtmlContainerInt HtmlContainer
        {
            get { return this._htmlContainer ?? (this._htmlContainer = this._parentBox != null ? this._parentBox.HtmlContainer : null); }
            set { this._htmlContainer = value; }
        }

        /// <summary>
        /// Gets or sets the parent box of this box
        /// </summary>
        public CssBox ParentBox
        {
            get { return this._parentBox; }
            set
            {
                //Remove from last parent
                if (this._parentBox != null)
                    this._parentBox.Boxes.Remove(this);

                this._parentBox = value;

                //Add to new parent
                if (value != null)
                    this._parentBox.Boxes.Add(this);
            }
        }

        /// <summary>
        /// Gets the children boxes of this box
        /// </summary>
        public List<CssBox> Boxes
        {
            get { return this._boxes; }
        }

        /// <summary>
        /// Is the box is of "br" element.
        /// </summary>
        public bool IsBrElement
        {
            get {
                return this._htmltag != null && this._htmltag.Name.Equals("br", StringComparison.InvariantCultureIgnoreCase);
            }
        }

        /// <summary>
        /// is the box "Display" is "Inline", is this is an inline box and not block.
        /// </summary>
        public bool IsInline
        {
            get { return (this.Display == CssConstants.Inline || this.Display == CssConstants.InlineBlock) && !this.IsBrElement; }
        }

        /// <summary>
        /// is the box "Display" is "Block", is this is an block box and not inline.
        /// </summary>
        public bool IsBlock
        {
            get { return this.Display == CssConstants.Block; }
        }

        /// <summary>
        /// Is the css box clickable (by default only "a" element is clickable)
        /// </summary>
        public virtual bool IsClickable
        {
            get { return this.HtmlTag != null && this.HtmlTag.Name == HtmlConstants.A && !this.HtmlTag.HasAttribute("id"); }
        }

        /// <summary>
        /// Gets a value indicating whether this instance or one of its parents has Position = fixed.
        /// </summary>
        /// <value>
        ///   <c>true</c> if this instance is fixed; otherwise, <c>false</c>.
        /// </value>
        public virtual bool IsFixed
        {
            get
            {
                if (this.Position == CssConstants.Fixed)
                    return true;

                if (this.ParentBox == null)
                    return false;

                CssBox parent = this;

                while (!(parent.ParentBox == null || parent == parent.ParentBox))
                {
                    parent = parent.ParentBox;

                    if (parent.Position == CssConstants.Fixed)
                        return true;
                }

                return false;
            }
        }

        /// <summary>
        /// Get the href link of the box (by default get "href" attribute)
        /// </summary>
        public virtual string HrefLink
        {
            get { return this.GetAttribute(HtmlConstants.Href); }
        }

        /// <summary>
        /// Gets the containing block-box of this box. (The nearest parent box with display=block)
        /// </summary>
        public CssBox ContainingBlock
        {
            get
            {
                if (this.ParentBox == null)
                {
                    return this; //This is the initial containing block.
                }

                var box = this.ParentBox;
                while (!box.IsBlock &&
                       box.Display != CssConstants.ListItem &&
                       box.Display != CssConstants.Table &&
                       box.Display != CssConstants.TableCell &&
                       box.ParentBox != null)
                {
                    box = box.ParentBox;
                }

                //Comment this following line to treat always superior box as block
                if (box == null)
                    throw new Exception("There's no containing block on the chain");

                return box;
            }
        }

        /// <summary>
        /// Gets the HTMLTag that hosts this box
        /// </summary>
        public HtmlTag HtmlTag
        {
            get { return this._htmltag; }
        }

        /// <summary>
        /// Gets if this box represents an image
        /// </summary>
        public bool IsImage
        {
            get { return this.Words.Count == 1 && this.Words[0].IsImage; }
        }

        /// <summary>
        /// Tells if the box is empty or contains just blank spaces
        /// </summary>
        public bool IsSpaceOrEmpty
        {
            get
            {
                if ((this.Words.Count != 0 || this.Boxes.Count != 0) && (this.Words.Count != 1 || !this.Words[0].IsSpaces))
                {
                    foreach (CssRect word in this.Words)
                    {
                        if (!word.IsSpaces)
                        {
                            return false;
                        }
                    }
                }
                return true;
            }
        }

        /// <summary>
        /// Gets or sets the inner text of the box
        /// </summary>
        public SubString Text
        {
            get { return this._text; }
            set
            {
                this._text = value;
                this._boxWords.Clear();
            }
        }

        /// <summary>
        /// Gets the line-boxes of this box (if block box)
        /// </summary>
        internal List<CssLineBox> LineBoxes
        {
            get { return this._lineBoxes; }
        }

        /// <summary>
        /// Gets the linebox(es) that contains words of this box (if inline)
        /// </summary>
        internal List<CssLineBox> ParentLineBoxes
        {
            get { return this._parentLineBoxes; }
        }

        /// <summary>
        /// Gets the rectangles where this box should be painted
        /// </summary>
        internal Dictionary<CssLineBox, RRect> Rectangles
        {
            get { return this._rectangles; }
        }

        /// <summary>
        /// Gets the BoxWords of text in the box
        /// </summary>
        internal List<CssRect> Words
        {
            get { return this._boxWords; }
        }

        /// <summary>
        /// Gets the first word of the box
        /// </summary>
        internal CssRect FirstWord
        {
            get { return this.Words[0]; }
        }

        /// <summary>
        /// Gets or sets the first linebox where content of this box appear
        /// </summary>
        internal CssLineBox FirstHostingLineBox
        {
            get { return this._firstHostingLineBox; }
            set { this._firstHostingLineBox = value; }
        }

        /// <summary>
        /// Gets or sets the last linebox where content of this box appear
        /// </summary>
        internal CssLineBox LastHostingLineBox
        {
            get { return this._lastHostingLineBox; }
            set { this._lastHostingLineBox = value; }
        }

        /// <summary>
        /// Create new css box for the given parent with the given html tag.<br/>
        /// </summary>
        /// <param name="tag">the html tag to define the box</param>
        /// <param name="parent">the box to add the new box to it as child</param>
        /// <returns>the new box</returns>
        public static CssBox CreateBox(HtmlTag tag, CssBox parent = null)
        {
            ArgChecker.AssertArgNotNull(tag, "tag");

            if (tag.Name == HtmlConstants.Img)
            {
                return new CssBoxImage(parent, tag);
            }
            else if (tag.Name == HtmlConstants.Iframe)
            {
                return new CssBoxFrame(parent, tag);
            }
            else if (tag.Name == HtmlConstants.Hr)
            {
                return new CssBoxHr(parent, tag);
            }
            else
            {
                return new CssBox(parent, tag);
            }
        }

        /// <summary>
        /// Create new css box for the given parent with the given optional html tag and insert it either
        /// at the end or before the given optional box.<br/>
        /// If no html tag is given the box will be anonymous.<br/>
        /// If no before box is given the new box will be added at the end of parent boxes collection.<br/>
        /// If before box doesn't exists in parent box exception is thrown.<br/>
        /// </summary>
        /// <remarks>
        /// To learn more about anonymous inline boxes visit: http://www.w3.org/TR/CSS21/visuren.html#anonymous
        /// </remarks>
        /// <param name="parent">the box to add the new box to it as child</param>
        /// <param name="tag">optional: the html tag to define the box</param>
        /// <param name="before">optional: to insert as specific location in parent box</param>
        /// <returns>the new box</returns>
        public static CssBox CreateBox(CssBox parent, HtmlTag tag = null, CssBox before = null)
        {
            ArgChecker.AssertArgNotNull(parent, "parent");

            var newBox = new CssBox(parent, tag);
            newBox.InheritStyle();
            if (before != null)
            {
                newBox.SetBeforeBox(before);
            }
            return newBox;
        }

        /// <summary>
        /// Create new css block box.
        /// </summary>
        /// <returns>the new block box</returns>
        public static CssBox CreateBlock()
        {
            var box = new CssBox(null, null);
            box.Display = CssConstants.Block;
            return box;
        }

        /// <summary>
        /// Create new css block box for the given parent with the given optional html tag and insert it either
        /// at the end or before the given optional box.<br/>
        /// If no html tag is given the box will be anonymous.<br/>
        /// If no before box is given the new box will be added at the end of parent boxes collection.<br/>
        /// If before box doesn't exists in parent box exception is thrown.<br/>
        /// </summary>
        /// <remarks>
        /// To learn more about anonymous block boxes visit CSS spec:
        /// http://www.w3.org/TR/CSS21/visuren.html#anonymous-block-level
        /// </remarks>
        /// <param name="parent">the box to add the new block box to it as child</param>
        /// <param name="tag">optional: the html tag to define the box</param>
        /// <param name="before">optional: to insert as specific location in parent box</param>
        /// <returns>the new block box</returns>
        public static CssBox CreateBlock(CssBox parent, HtmlTag tag = null, CssBox before = null)
        {
            ArgChecker.AssertArgNotNull(parent, "parent");

            var newBox = CreateBox(parent, tag, before);
            newBox.Display = CssConstants.Block;
            return newBox;
        }

        /// <summary>
        /// Measures the bounds of box and children, recursively.<br/>
        /// Performs layout of the DOM structure creating lines by set bounds restrictions.
        /// </summary>
        /// <param name="g">Device context to use</param>
        public void PerformLayout(RGraphics g)
        {
            try
            {
                this.PerformLayoutImp(g);
            }
            catch (Exception ex)
            {
                this.HtmlContainer.ReportError(HtmlRenderErrorType.Layout, "Exception in box layout", ex);
            }
        }

        /// <summary>
        /// Paints the fragment
        /// </summary>
        /// <param name="g">Device context to use</param>
        public void Paint(RGraphics g)
        {
            try
            {
                if (this.Display != CssConstants.None && this.Visibility == CssConstants.Visible)
                {
                    // use initial clip to draw blocks with Position = fixed. I.e. ignrore page margins
                    if (this.Position == CssConstants.Fixed)
                    {
                        g.SuspendClipping();
                    }

                    // don't call paint if the rectangle of the box is not in visible rectangle
                    bool visible = this.Rectangles.Count == 0;
                    if (!visible)
                    {
                        var clip = g.GetClip();
                        var rect = this.ContainingBlock.ClientRectangle;
                        rect.X -= 2;
                        rect.Width += 2;
                        if (!this.IsFixed)
                        {
                            //rect.Offset(new RPoint(-HtmlContainer.Location.X, -HtmlContainer.Location.Y));
                            rect.Offset(this.HtmlContainer.ScrollOffset);
                        }
                        clip.Intersect(rect);

                        if (clip != RRect.Empty)
                            visible = true;
                    }

                    if (visible)
                        this.PaintImp(g);

                    // Restore clips
                    if (this.Position == CssConstants.Fixed)
                    {
                        g.ResumeClipping();
                    }

                }
            }
            catch (Exception ex)
            {
                this.HtmlContainer.ReportError(HtmlRenderErrorType.Paint, "Exception in box paint", ex);
            }
        }

        /// <summary>
        /// Set this box in 
        /// </summary>
        /// <param name="before"></param>
        public void SetBeforeBox(CssBox before)
        {
            int index = this._parentBox.Boxes.IndexOf(before);
            if (index < 0)
                throw new Exception("before box doesn't exist on parent");

            this._parentBox.Boxes.Remove(this);
            this._parentBox.Boxes.Insert(index, this);
        }

        /// <summary>
        /// Move all child boxes from <paramref name="fromBox"/> to this box.
        /// </summary>
        /// <param name="fromBox">the box to move all its child boxes from</param>
        public void SetAllBoxes(CssBox fromBox)
        {
            foreach (var childBox in fromBox._boxes)
                childBox._parentBox = this;

            this._boxes.AddRange(fromBox._boxes);
            fromBox._boxes.Clear();
        }

        /// <summary>
        /// Splits the text into words and saves the result
        /// </summary>
        public void ParseToWords()
        {
            this._boxWords.Clear();

            int startIdx = 0;
            bool preserveSpaces = this.WhiteSpace == CssConstants.Pre || this.WhiteSpace == CssConstants.PreWrap;
            bool respoctNewline = preserveSpaces || this.WhiteSpace == CssConstants.PreLine;
            while (startIdx < this._text.Length)
            {
                while (startIdx < this._text.Length && this._text[startIdx] == '\r')
                    startIdx++;

                if (startIdx < this._text.Length)
                {
                    var endIdx = startIdx;
                    while (endIdx < this._text.Length && char.IsWhiteSpace(this._text[endIdx]) && this._text[endIdx] != '\n')
                        endIdx++;

                    if (endIdx > startIdx)
                    {
                        if (preserveSpaces)
                            this._boxWords.Add(new CssRectWord(this, HtmlUtils.DecodeHtml(this._text.Substring(startIdx, endIdx - startIdx)), false, false));
                    }
                    else
                    {
                        endIdx = startIdx;
                        while (endIdx < this._text.Length && !char.IsWhiteSpace(this._text[endIdx]) && this._text[endIdx] != '-' && this.WordBreak != CssConstants.BreakAll && !CommonUtils.IsAsianCharecter(this._text[endIdx]))
                            endIdx++;

                        if (endIdx < this._text.Length && (this._text[endIdx] == '-' || this.WordBreak == CssConstants.BreakAll || CommonUtils.IsAsianCharecter(this._text[endIdx])))
                            endIdx++;

                        if (endIdx > startIdx)
                        {
                            var hasSpaceBefore = !preserveSpaces && (startIdx > 0 && this._boxWords.Count == 0 && char.IsWhiteSpace(this._text[startIdx - 1]));
                            var hasSpaceAfter = !preserveSpaces && (endIdx < this._text.Length && char.IsWhiteSpace(this._text[endIdx]));
                            this._boxWords.Add(new CssRectWord(this, HtmlUtils.DecodeHtml(this._text.Substring(startIdx, endIdx - startIdx)), hasSpaceBefore, hasSpaceAfter));
                        }
                    }

                    // create new-line word so it will effect the layout
                    if (endIdx < this._text.Length && this._text[endIdx] == '\n')
                    {
                        endIdx++;
                        if (respoctNewline)
                            this._boxWords.Add(new CssRectWord(this, "\n", false, false));
                    }

                    startIdx = endIdx;
                }
            }
        }

        /// <summary>
        /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
        /// </summary>
        public virtual void Dispose()
        {
            if (this._imageLoadHandler != null)
                this._imageLoadHandler.Dispose();

            foreach (var childBox in this.Boxes)
            {
                childBox.Dispose();
            }
        }


        #region Private Methods

        /// <summary>
        /// Measures the bounds of box and children, recursively.<br/>
        /// Performs layout of the DOM structure creating lines by set bounds restrictions.<br/>
        /// </summary>
        /// <param name="g">Device context to use</param>
        protected virtual void PerformLayoutImp(RGraphics g)
        {
            if (this.Display != CssConstants.None)
            {
                this.RectanglesReset();
                this.MeasureWordsSize(g);
            }

            if (this.IsBlock || this.Display == CssConstants.ListItem || this.Display == CssConstants.Table || this.Display == CssConstants.InlineTable || this.Display == CssConstants.TableCell)
            {
                // Because their width and height are set by CssTable
                if (this.Display != CssConstants.TableCell && this.Display != CssConstants.Table)
                {
                    double width = this.ContainingBlock.Size.Width
                                   - this.ContainingBlock.ActualPaddingLeft - this.ContainingBlock.ActualPaddingRight
                                   - this.ContainingBlock.ActualBorderLeftWidth - this.ContainingBlock.ActualBorderRightWidth;

                    if (this.Width != CssConstants.Auto && !string.IsNullOrEmpty(this.Width))
                    {
                        width = CssValueParser.ParseLength(this.Width, width, this);
                    }

                    this.Size = new RSize(width, this.Size.Height);

                    // must be separate because the margin can be calculated by percentage of the width
                    this.Size = new RSize(width - this.ActualMarginLeft - this.ActualMarginRight, this.Size.Height);
                }

                if (this.Display != CssConstants.TableCell)
                {
                    var prevSibling = DomUtils.GetPreviousSibling(this);
                    double left;
                    double top;

                    if (this.Position == CssConstants.Fixed)
                    {
                        left = 0;
                        top = 0;
                    }
                    else
                    {
                        left = this.ContainingBlock.Location.X + this.ContainingBlock.ActualPaddingLeft + this.ActualMarginLeft + this.ContainingBlock.ActualBorderLeftWidth;
                        top = (prevSibling == null && this.ParentBox != null ? this.ParentBox.ClientTop : this.ParentBox == null ? this.Location.Y : 0) + this.MarginTopCollapse(prevSibling) + (prevSibling != null ? prevSibling.ActualBottom + prevSibling.ActualBorderBottomWidth : 0);
                        this.Location = new RPoint(left, top);
                        this.ActualBottom = top;
                    }
                }

                //If we're talking about a table here..
                if (this.Display == CssConstants.Table || this.Display == CssConstants.InlineTable)
                {
                    CssLayoutEngineTable.PerformLayout(g, this);
                }
                else
                {
                    //If there's just inline boxes, create LineBoxes
                    if (DomUtils.ContainsInlinesOnly(this))
                    {
                        this.ActualBottom = this.Location.Y;
                        CssLayoutEngine.CreateLineBoxes(g, this); //This will automatically set the bottom of this block
                    }
                    else if (this._boxes.Count > 0)
                    {
                        foreach (var childBox in this.Boxes)
                        {
                            childBox.PerformLayout(g);
                        }
                        this.ActualRight = this.CalculateActualRight();
                        this.ActualBottom = this.MarginBottomCollapse();
                    }
                }
            }
            else
            {
                var prevSibling = DomUtils.GetPreviousSibling(this);
                if (prevSibling != null)
                {
                    if (this.Location == RPoint.Empty)
                        this.Location = prevSibling.Location;
                    this.ActualBottom = prevSibling.ActualBottom;
                }
            }
            this.ActualBottom = Math.Max(this.ActualBottom, this.Location.Y + this.ActualHeight);

            this.CreateListItemBox(g);

            if (!this.IsFixed)
            {
                var actualWidth = Math.Max(this.GetMinimumWidth() + GetWidthMarginDeep(this), this.Size.Width < 90999 ? this.ActualRight - this.HtmlContainer.Root.Location.X : 0);
                this.HtmlContainer.ActualSize = CommonUtils.Max(this.HtmlContainer.ActualSize, new RSize(actualWidth, this.ActualBottom - this.HtmlContainer.Root.Location.Y));
            }
        }

        /// <summary>
        /// Assigns words its width and height
        /// </summary>
        /// <param name="g"></param>
        internal virtual void MeasureWordsSize(RGraphics g)
        {
            if (!this._wordsSizeMeasured)
            {
                if (this.BackgroundImage != CssConstants.None && this._imageLoadHandler == null)
                {
                    this._imageLoadHandler = new ImageLoadHandler(this.HtmlContainer, this.OnImageLoadComplete);
                    this._imageLoadHandler.LoadImage(this.BackgroundImage, this.HtmlTag != null ? this.HtmlTag.Attributes : null);
                }

                this.MeasureWordSpacing(g);

                if (this.Words.Count > 0)
                {
                    foreach (var boxWord in this.Words)
                    {
                        boxWord.Width = boxWord.Text != "\n" ? g.MeasureString(boxWord.Text, this.ActualFont).Width : 0;
                        boxWord.Height = this.ActualFont.Height;
                    }
                }

                this._wordsSizeMeasured = true;
            }
        }

        /// <summary>
        /// Get the parent of this css properties instance.
        /// </summary>
        /// <returns></returns>
        protected override sealed CssBoxProperties GetParent()
        {
            return this._parentBox;
        }

        /// <summary>
        /// Gets the index of the box to be used on a (ordered) list
        /// </summary>
        /// <returns></returns>
        private int GetIndexForList()
        {
            bool reversed = !string.IsNullOrEmpty(this.ParentBox.GetAttribute("reversed"));
            int index;
            if (!int.TryParse(this.ParentBox.GetAttribute("start"), out index))
            {
                if (reversed)
                {
                    index = 0;
                    foreach (CssBox b in this.ParentBox.Boxes)
                    {
                        if (b.Display == CssConstants.ListItem)
                            index++;
                    }
                }
                else
                {
                    index = 1;
                }
            }

            foreach (CssBox b in this.ParentBox.Boxes)
            {
                if (b.Equals(this))
                    return index;

                if (b.Display == CssConstants.ListItem)
                    index += reversed ? -1 : 1;
            }

            return index;
        }

        /// <summary>
        /// Creates the <see cref="_listItemBox"/>
        /// </summary>
        /// <param name="g"></param>
        private void CreateListItemBox(RGraphics g)
        {
            if (this.Display == CssConstants.ListItem && this.ListStyleType != CssConstants.None)
            {
                if (this._listItemBox == null)
                {
                    this._listItemBox = new CssBox(null, null);
                    this._listItemBox.InheritStyle(this);
                    this._listItemBox.Display = CssConstants.Inline;
                    this._listItemBox.HtmlContainer = this.HtmlContainer;

                    if (this.ListStyleType.Equals(CssConstants.Disc, StringComparison.InvariantCultureIgnoreCase))
                    {
                        this._listItemBox.Text = new SubString("•");
                    }
                    else if (this.ListStyleType.Equals(CssConstants.Circle, StringComparison.InvariantCultureIgnoreCase))
                    {
                        this._listItemBox.Text = new SubString("o");
                    }
                    else if (this.ListStyleType.Equals(CssConstants.Square, StringComparison.InvariantCultureIgnoreCase))
                    {
                        this._listItemBox.Text = new SubString("♠");
                    }
                    else if (this.ListStyleType.Equals(CssConstants.Decimal, StringComparison.InvariantCultureIgnoreCase))
                    {
                        this._listItemBox.Text = new SubString(this.GetIndexForList().ToString(CultureInfo.InvariantCulture) + ".");
                    }
                    else if (this.ListStyleType.Equals(CssConstants.DecimalLeadingZero, StringComparison.InvariantCultureIgnoreCase))
                    {
                        this._listItemBox.Text = new SubString(this.GetIndexForList().ToString("00", CultureInfo.InvariantCulture) + ".");
                    }
                    else
                    {
                        this._listItemBox.Text = new SubString(CommonUtils.ConvertToAlphaNumber(this.GetIndexForList(), this.ListStyleType) + ".");
                    }

                    this._listItemBox.ParseToWords();

                    this._listItemBox.PerformLayoutImp(g);
                    this._listItemBox.Size = new RSize(this._listItemBox.Words[0].Width, this._listItemBox.Words[0].Height);
                }
                this._listItemBox.Words[0].Left = this.Location.X - this._listItemBox.Size.Width - 5;
                this._listItemBox.Words[0].Top = this.Location.Y + this.ActualPaddingTop; // +FontAscent;
            }
        }

        /// <summary>
        /// Searches for the first word occurrence inside the box, on the specified linebox
        /// </summary>
        /// <param name="b"></param>
        /// <param name="line"> </param>
        /// <returns></returns>
        internal CssRect FirstWordOccourence(CssBox b, CssLineBox line)
        {
            if (b.Words.Count == 0 && b.Boxes.Count == 0)
            {
                return null;
            }

            if (b.Words.Count > 0)
            {
                foreach (CssRect word in b.Words)
                {
                    if (line.Words.Contains(word))
                    {
                        return word;
                    }
                }
                return null;
            }
            else
            {
                foreach (CssBox bb in b.Boxes)
                {
                    CssRect w = this.FirstWordOccourence(bb, line);

                    if (w != null)
                    {
                        return w;
                    }
                }

                return null;
            }
        }

        /// <summary>
        /// Gets the specified Attribute, returns string.Empty if no attribute specified
        /// </summary>
        /// <param name="attribute">Attribute to retrieve</param>
        /// <returns>Attribute value or string.Empty if no attribute specified</returns>
        internal string GetAttribute(string attribute)
        {
            return this.GetAttribute(attribute, string.Empty);
        }

        /// <summary>
        /// Gets the value of the specified attribute of the source HTML tag.
        /// </summary>
        /// <param name="attribute">Attribute to retrieve</param>
        /// <param name="defaultValue">Value to return if attribute is not specified</param>
        /// <returns>Attribute value or defaultValue if no attribute specified</returns>
        internal string GetAttribute(string attribute, string defaultValue)
        {
            return this.HtmlTag != null ? this.HtmlTag.TryGetAttribute(attribute, defaultValue) : defaultValue;
        }

        /// <summary>
        /// Gets the minimum width that the box can be.<br/>
        /// The box can be as thin as the longest word plus padding.<br/>
        /// The check is deep thru box tree.<br/>
        /// </summary>
        /// <returns>the min width of the box</returns>
        internal double GetMinimumWidth()
        {
            double maxWidth = 0;
            CssRect maxWidthWord = null;
            GetMinimumWidth_LongestWord(this, ref maxWidth, ref maxWidthWord);

            double padding = 0f;
            if (maxWidthWord != null)
            {
                var box = maxWidthWord.OwnerBox;
                while (box != null)
                {
                    padding += box.ActualBorderRightWidth + box.ActualPaddingRight + box.ActualBorderLeftWidth + box.ActualPaddingLeft;
                    box = box != this ? box.ParentBox : null;
                }
            }

            return maxWidth + padding;
        }

        /// <summary>
        /// Gets the longest word (in width) inside the box, deeply.
        /// </summary>
        /// <param name="box"></param>
        /// <param name="maxWidth"> </param>
        /// <param name="maxWidthWord"> </param>
        /// <returns></returns>
        private static void GetMinimumWidth_LongestWord(CssBox box, ref double maxWidth, ref CssRect maxWidthWord)
        {
            if (box.Words.Count > 0)
            {
                foreach (CssRect cssRect in box.Words)
                {
                    if (cssRect.Width > maxWidth)
                    {
                        maxWidth = cssRect.Width;
                        maxWidthWord = cssRect;
                    }
                }
            }
            else
            {
                foreach (CssBox childBox in box.Boxes)
                    GetMinimumWidth_LongestWord(childBox, ref maxWidth, ref maxWidthWord);
            }
        }

        /// <summary>
        /// Get the total margin value (left and right) from the given box to the given end box.<br/>
        /// </summary>
        /// <param name="box">the box to start calculation from.</param>
        /// <returns>the total margin</returns>
        private static double GetWidthMarginDeep(CssBox box)
        {
            double sum = 0f;
            if (box.Size.Width > 90999 || (box.ParentBox != null && box.ParentBox.Size.Width > 90999))
            {
                while (box != null)
                {
                    sum += box.ActualMarginLeft + box.ActualMarginRight;
                    box = box.ParentBox;
                }
            }
            return sum;
        }

        /// <summary>
        /// Gets the maximum bottom of the boxes inside the startBox
        /// </summary>
        /// <param name="startBox"></param>
        /// <param name="currentMaxBottom"></param>
        /// <returns></returns>
        internal double GetMaximumBottom(CssBox startBox, double currentMaxBottom)
        {
            foreach (var line in startBox.Rectangles.Keys)
            {
                currentMaxBottom = Math.Max(currentMaxBottom, startBox.Rectangles[line].Bottom);
            }

            foreach (var b in startBox.Boxes)
            {
                currentMaxBottom = Math.Max(currentMaxBottom, this.GetMaximumBottom(b, currentMaxBottom));
            }

            return currentMaxBottom;
        }

        /// <summary>
        /// Get the <paramref name="minWidth"/> and <paramref name="maxWidth"/> width of the box content.<br/>
        /// </summary>
        /// <param name="minWidth">The minimum width the content must be so it won't overflow (largest word + padding).</param>
        /// <param name="maxWidth">The total width the content can take without line wrapping (with padding).</param>
        internal void GetMinMaxWidth(out double minWidth, out double maxWidth)
        {
            double min = 0f;
            double maxSum = 0f;
            double paddingSum = 0f;
            double marginSum = 0f;
            GetMinMaxSumWords(this, ref min, ref maxSum, ref paddingSum, ref marginSum);

            maxWidth = paddingSum + maxSum;
            minWidth = paddingSum + (min < 90999 ? min : 0);
        }

        /// <summary>
        /// Get the <paramref name="min"/> and <paramref name="maxSum"/> of the box words content and <paramref name="paddingSum"/>.<br/>
        /// </summary>
        /// <param name="box">the box to calculate for</param>
        /// <param name="min">the width that allows for each word to fit (width of the longest word)</param>
        /// <param name="maxSum">the max width a single line of words can take without wrapping</param>
        /// <param name="paddingSum">the total amount of padding the content has </param>
        /// <param name="marginSum"></param>
        /// <returns></returns>
        private static void GetMinMaxSumWords(CssBox box, ref double min, ref double maxSum, ref double paddingSum, ref double marginSum)
        {
            double? oldSum = null;

            // not inline (block) boxes start a new line so we need to reset the max sum
            if (box.Display != CssConstants.Inline && box.Display != CssConstants.TableCell && box.WhiteSpace != CssConstants.NoWrap)
            {
                oldSum = maxSum;
                maxSum = marginSum;
            }

            // add the padding 
            paddingSum += box.ActualBorderLeftWidth + box.ActualBorderRightWidth + box.ActualPaddingRight + box.ActualPaddingLeft;


            // for tables the padding also contains the spacing between cells
            if (box.Display == CssConstants.Table)
                paddingSum += CssLayoutEngineTable.GetTableSpacing(box);

            if (box.Words.Count > 0)
            {
                // calculate the min and max sum for all the words in the box
                foreach (CssRect word in box.Words)
                {
                    maxSum += word.FullWidth + (word.HasSpaceBefore ? word.OwnerBox.ActualWordSpacing : 0);
                    min = Math.Max(min, word.Width);
                }

                // remove the last word padding
                if (box.Words.Count > 0 && !box.Words[box.Words.Count - 1].HasSpaceAfter)
                    maxSum -= box.Words[box.Words.Count - 1].ActualWordSpacing;
            }
            else
            {
                // recursively on all the child boxes
                for (int i = 0; i < box.Boxes.Count; i++)
                {
                    CssBox childBox = box.Boxes[i];
                    marginSum += childBox.ActualMarginLeft + childBox.ActualMarginRight;

                    //maxSum += childBox.ActualMarginLeft + childBox.ActualMarginRight;
                    GetMinMaxSumWords(childBox, ref min, ref maxSum, ref paddingSum, ref marginSum);

                    marginSum -= childBox.ActualMarginLeft + childBox.ActualMarginRight;
                }
            }

            // max sum is max of all the lines in the box
            if (oldSum.HasValue)
            {
                maxSum = Math.Max(maxSum, oldSum.Value);
            }
        }

        /// <summary>
        /// Gets if this box has only inline siblings (including itself)
        /// </summary>
        /// <returns></returns>
        internal bool HasJustInlineSiblings()
        {
            return this.ParentBox != null && DomUtils.ContainsInlinesOnly(this.ParentBox);
        }

        /// <summary>
        /// Gets the rectangles where inline box will be drawn. See Remarks for more info.
        /// </summary>
        /// <returns>Rectangles where content should be placed</returns>
        /// <remarks>
        /// Inline boxes can be split across different LineBoxes, that's why this method
        /// Delivers a rectangle for each LineBox related to this box, if inline.
        /// </remarks>
        /// <summary>
        /// Inherits inheritable values from parent.
        /// </summary>
        internal new void InheritStyle(CssBox box = null, bool everything = false)
        {
            base.InheritStyle(box ?? this.ParentBox, everything);
        }

        /// <summary>
        /// Gets the result of collapsing the vertical margins of the two boxes
        /// </summary>
        /// <param name="prevSibling">the previous box under the same parent</param>
        /// <returns>Resulting top margin</returns>
        protected double MarginTopCollapse(CssBoxProperties prevSibling)
        {
            double value;
            if (prevSibling != null)
            {
                value = Math.Max(prevSibling.ActualMarginBottom, this.ActualMarginTop);
                this.CollapsedMarginTop = value;
            }
            else if (this._parentBox != null && this.ActualPaddingTop < 0.1 && this.ActualPaddingBottom < 0.1 && this._parentBox.ActualPaddingTop < 0.1 && this._parentBox.ActualPaddingBottom < 0.1)
            {
                value = Math.Max(0, this.ActualMarginTop - Math.Max(this._parentBox.ActualMarginTop, this._parentBox.CollapsedMarginTop));
            }
            else
            {
                value = this.ActualMarginTop;
            }

            // fix for hr tag
            if (value < 0.1 && this.HtmlTag != null && this.HtmlTag.Name == "hr")
            {
                value = this.GetEmHeight() * 1.1f;
            }

            return value;
        }

        public bool BreakPage()
        {
            var container = this.HtmlContainer;

            if (this.Size.Height >= container.PageSize.Height)
                return false;

            var remTop = (this.Location.Y - container.MarginTop) % container.PageSize.Height;
            var remBottom = (this.ActualBottom - container.MarginTop) % container.PageSize.Height;

            if (remTop > remBottom)
            {
                var diff = container.PageSize.Height - remTop;
                this.Location = new RPoint(this.Location.X, this.Location.Y + diff + 1);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Calculate the actual right of the box by the actual right of the child boxes if this box actual right is not set.
        /// </summary>
        /// <returns>the calculated actual right value</returns>
        private double CalculateActualRight()
        {
            if (this.ActualRight > 90999)
            {
                var maxRight = 0d;
                foreach (var box in this.Boxes)
                {
                    maxRight = Math.Max(maxRight, box.ActualRight + box.ActualMarginRight);
                }
                return maxRight + this.ActualPaddingRight + this.ActualMarginRight + this.ActualBorderRightWidth;
            }
            else
            {
                return this.ActualRight;
            }
        }

        /// <summary>
        /// Gets the result of collapsing the vertical margins of the two boxes
        /// </summary>
        /// <returns>Resulting bottom margin</returns>
        private double MarginBottomCollapse()
        {
            double margin = 0;
            if (this.ParentBox != null && this.ParentBox.Boxes.IndexOf(this) == this.ParentBox.Boxes.Count - 1 && this._parentBox.ActualMarginBottom < 0.1)
            {
                var lastChildBottomMargin = this._boxes[this._boxes.Count - 1].ActualMarginBottom;
                margin = this.Height == "auto" ? Math.Max(this.ActualMarginBottom, lastChildBottomMargin) : lastChildBottomMargin;
            }
            return Math.Max(this.ActualBottom, this._boxes[this._boxes.Count - 1].ActualBottom + margin + this.ActualPaddingBottom + this.ActualBorderBottomWidth);
        }

        /// <summary>
        /// Deeply offsets the top of the box and its contents
        /// </summary>
        /// <param name="amount"></param>
        internal void OffsetTop(double amount)
        {
            List<CssLineBox> lines = new List<CssLineBox>();
            foreach (CssLineBox line in this.Rectangles.Keys)
                lines.Add(line);

            foreach (CssLineBox line in lines)
            {
                RRect r = this.Rectangles[line];
                this.Rectangles[line] = new RRect(r.X, r.Y + amount, r.Width, r.Height);
            }

            foreach (CssRect word in this.Words)
            {
                word.Top += amount;
            }

            foreach (CssBox b in this.Boxes)
            {
                b.OffsetTop(amount);
            }

            if (this._listItemBox != null)
                this._listItemBox.OffsetTop(amount);

            this.Location = new RPoint(this.Location.X, this.Location.Y + amount);
        }

        /// <summary>
        /// Paints the fragment
        /// </summary>
        /// <param name="g">the device to draw to</param>
        protected virtual void PaintImp(RGraphics g)
        {
            if (this.Display != CssConstants.None && (this.Display != CssConstants.TableCell || this.EmptyCells != CssConstants.Hide || !this.IsSpaceOrEmpty))
            {
                var clipped = RenderUtils.ClipGraphicsByOverflow(g, this);

                var areas = this.Rectangles.Count == 0 ? new List<RRect>(new[] { this.Bounds }) : new List<RRect>(this.Rectangles.Values);
                var clip = g.GetClip();
                RRect[] rects = areas.ToArray();
                RPoint offset = RPoint.Empty;
                if (!this.IsFixed)
                {
                    offset = this.HtmlContainer.ScrollOffset;
                }

                for (int i = 0; i < rects.Length; i++)
                {
                    var actualRect = rects[i];
                    actualRect.Offset(offset);

                    if (this.IsRectVisible(actualRect, clip))
                    {
                        this.PaintBackground(g, actualRect, i == 0, i == rects.Length - 1);
                        BordersDrawHandler.DrawBoxBorders(g, this, actualRect, i == 0, i == rects.Length - 1);
                    }
                }

                this.PaintWords(g, offset);

                for (int i = 0; i < rects.Length; i++)
                {
                    var actualRect = rects[i];
                    actualRect.Offset(offset);

                    if (this.IsRectVisible(actualRect, clip))
                    {
                        this.PaintDecoration(g, actualRect, i == 0, i == rects.Length - 1);
                    }
                }

                // split paint to handle z-order
                foreach (CssBox b in this.Boxes)
                {
                    if (b.Position != CssConstants.Absolute && !b.IsFixed)
                        b.Paint(g);
                }
                foreach (CssBox b in this.Boxes)
                {
                    if (b.Position == CssConstants.Absolute)
                        b.Paint(g);
                }
                foreach (CssBox b in this.Boxes)
                {
                    if (b.IsFixed)
                        b.Paint(g);
                }

                if (clipped)
                    g.PopClip();

                if (this._listItemBox != null)
                {
                    this._listItemBox.Paint(g);
                }
            }
        }

        private bool IsRectVisible(RRect rect, RRect clip)
        {
            rect.X -= 2;
            rect.Width += 2;
            clip.Intersect(rect);

            if (clip != RRect.Empty)
                return true;

            return false;
        }

        /// <summary>
        /// Paints the background of the box
        /// </summary>
        /// <param name="g">the device to draw into</param>
        /// <param name="rect">the bounding rectangle to draw in</param>
        /// <param name="isFirst">is it the first rectangle of the element</param>
        /// <param name="isLast">is it the last rectangle of the element</param>
        protected void PaintBackground(RGraphics g, RRect rect, bool isFirst, bool isLast)
        {
            if (rect.Width > 0 && rect.Height > 0)
            {
                RBrush brush = null;

                if (this.BackgroundGradient != CssConstants.None)
                {
                    brush = g.GetLinearGradientBrush(rect, this.ActualBackgroundColor, this.ActualBackgroundGradient, this.ActualBackgroundGradientAngle);
                }
                else if (RenderUtils.IsColorVisible(this.ActualBackgroundColor))
                {
                    brush = g.GetSolidBrush(this.ActualBackgroundColor);
                }

                if (brush != null)
                {
                    // TODO:a handle it correctly (tables background)
                    // if (isLast)
                    //  rectangle.Width -= ActualWordSpacing + CssUtils.GetWordEndWhitespace(ActualFont);

                    RGraphicsPath roundrect = null;
                    if (this.IsRounded)
                    {
                        roundrect = RenderUtils.GetRoundRect(g, rect, this.ActualCornerNw, this.ActualCornerNe, this.ActualCornerSe, this.ActualCornerSw);
                    }

                    Object prevMode = null;
                    if (this.HtmlContainer != null && !this.HtmlContainer.AvoidGeometryAntialias && this.IsRounded)
                    {
                        prevMode = g.SetAntiAliasSmoothingMode();
                    }

                    if (roundrect != null)
                    {
                        g.DrawPath(brush, roundrect);
                    }
                    else
                    {
                        g.DrawRectangle(brush, Math.Ceiling(rect.X), Math.Ceiling(rect.Y), rect.Width, rect.Height);
                    }

                    g.ReturnPreviousSmoothingMode(prevMode);

                    if (roundrect != null)
                        roundrect.Dispose();
                    brush.Dispose();
                }

                if (this._imageLoadHandler != null && this._imageLoadHandler.Image != null && isFirst)
                {
                    BackgroundImageDrawHandler.DrawBackgroundImage(g, this, this._imageLoadHandler, rect);
                }
            }
        }

        /// <summary>
        /// Paint all the words in the box.
        /// </summary>
        /// <param name="g">the device to draw into</param>
        /// <param name="offset">the current scroll offset to offset the words</param>
        private void PaintWords(RGraphics g, RPoint offset)
        {
            if (this.Width.Length > 0)
            {
                var isRtl = this.Direction == CssConstants.Rtl;
                foreach (var word in this.Words)
                {
                    if (!word.IsLineBreak)
                    {
                        var clip = g.GetClip();
                        var wordRect = word.Rectangle;
                        wordRect.Offset(offset);
                        clip.Intersect(wordRect);

                        if (clip != RRect.Empty)
                        {
                            var wordPoint = new RPoint(word.Left + offset.X, word.Top + offset.Y);
                            if (word.Selected)
                            {
                                // handle paint selected word background and with partial word selection
                                var wordLine = DomUtils.GetCssLineBoxByWord(word);
                                var left = word.SelectedStartOffset > -1 ? word.SelectedStartOffset : (wordLine.Words[0] != word && word.HasSpaceBefore ? -this.ActualWordSpacing : 0);
                                var padWordRight = word.HasSpaceAfter && !wordLine.IsLastSelectedWord(word);
                                var width = word.SelectedEndOffset > -1 ? word.SelectedEndOffset : word.Width + (padWordRight ? this.ActualWordSpacing : 0);
                                var rect = new RRect(word.Left + offset.X + left, word.Top + offset.Y, width - left, wordLine.LineHeight);

                                g.DrawRectangle(this.GetSelectionBackBrush(g, false), rect.X, rect.Y, rect.Width, rect.Height);

                                if (this.HtmlContainer.SelectionForeColor != RColor.Empty && (word.SelectedStartOffset > 0 || word.SelectedEndIndexOffset > -1))
                                {
                                    g.PushClipExclude(rect);
                                    g.DrawString(word.Text, this.ActualFont, this.ActualColor, wordPoint, new RSize(word.Width, word.Height), isRtl);
                                    g.PopClip();
                                    g.PushClip(rect);
                                    g.DrawString(word.Text, this.ActualFont, this.GetSelectionForeBrush(), wordPoint, new RSize(word.Width, word.Height), isRtl);
                                    g.PopClip();
                                }
                                else
                                {
                                    g.DrawString(word.Text, this.ActualFont, this.GetSelectionForeBrush(), wordPoint, new RSize(word.Width, word.Height), isRtl);
                                }
                            }
                            else
                            {
                                //                            g.DrawRectangle(HtmlContainer.Adapter.GetPen(RColor.Black), wordPoint.X, wordPoint.Y, word.Width - 1, word.Height - 1);
                                g.DrawString(word.Text, this.ActualFont, this.ActualColor, wordPoint, new RSize(word.Width, word.Height), isRtl);
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Paints the text decoration (underline/strike-through/over-line)
        /// </summary>
        /// <param name="g">the device to draw into</param>
        /// <param name="rectangle"> </param>
        /// <param name="isFirst"> </param>
        /// <param name="isLast"> </param>
        protected void PaintDecoration(RGraphics g, RRect rectangle, bool isFirst, bool isLast)
        {
            if (string.IsNullOrEmpty(this.TextDecoration) || this.TextDecoration == CssConstants.None)
                return;

            double y = 0f;
            if (this.TextDecoration == CssConstants.Underline)
            {
                y = Math.Round(rectangle.Top + this.ActualFont.UnderlineOffset);
            }
            else if (this.TextDecoration == CssConstants.LineThrough)
            {
                y = rectangle.Top + rectangle.Height / 2f;
            }
            else if (this.TextDecoration == CssConstants.Overline)
            {
                y = rectangle.Top;
            }
            y -= this.ActualPaddingBottom - this.ActualBorderBottomWidth;

            double x1 = rectangle.X;
            if (isFirst)
                x1 += this.ActualPaddingLeft + this.ActualBorderLeftWidth;

            double x2 = rectangle.Right;
            if (isLast)
                x2 -= this.ActualPaddingRight + this.ActualBorderRightWidth;

            var pen = g.GetPen(this.ActualColor);
            pen.Width = 1;
            pen.DashStyle = RDashStyle.Solid;
            g.DrawLine(pen, x1, y, x2, y);
        }

        /// <summary>
        /// Offsets the rectangle of the specified linebox by the specified gap,
        /// and goes deep for rectangles of children in that linebox.
        /// </summary>
        /// <param name="lineBox"></param>
        /// <param name="gap"></param>
        internal void OffsetRectangle(CssLineBox lineBox, double gap)
        {
            if (this.Rectangles.ContainsKey(lineBox))
            {
                var r = this.Rectangles[lineBox];
                this.Rectangles[lineBox] = new RRect(r.X, r.Y + gap, r.Width, r.Height);
            }
        }

        /// <summary>
        /// Resets the <see cref="Rectangles"/> array
        /// </summary>
        internal void RectanglesReset()
        {
            this._rectangles.Clear();
        }

        /// <summary>
        /// On image load process complete with image request refresh for it to be painted.
        /// </summary>
        /// <param name="image">the image loaded or null if failed</param>
        /// <param name="rectangle">the source rectangle to draw in the image (empty - draw everything)</param>
        /// <param name="async">is the callback was called async to load image call</param>
        private void OnImageLoadComplete(RImage image, RRect rectangle, bool async)
        {
            if (image != null && async)
                this.HtmlContainer.RequestRefresh(false);
        }

        /// <summary>
        /// Get brush for the text depending if there is selected text color set.
        /// </summary>
        protected RColor GetSelectionForeBrush()
        {
            return this.HtmlContainer.SelectionForeColor != RColor.Empty ? this.HtmlContainer.SelectionForeColor : this.ActualColor;
        }

        /// <summary>
        /// Get brush for selection background depending if it has external and if alpha is required for images.
        /// </summary>
        /// <param name="g"></param>
        /// <param name="forceAlpha">used for images so they will have alpha effect</param>
        protected RBrush GetSelectionBackBrush(RGraphics g, bool forceAlpha)
        {
            var backColor = this.HtmlContainer.SelectionBackColor;
            if (backColor != RColor.Empty)
            {
                if (forceAlpha && backColor.A > 180)
                    return g.GetSolidBrush(RColor.FromArgb(180, backColor.R, backColor.G, backColor.B));
                else
                    return g.GetSolidBrush(backColor);
            }
            else
            {
                return g.GetSolidBrush(CssUtils.DefaultSelectionBackcolor);
            }
        }

        protected override RFont GetCachedFont(string fontFamily, double fsize, RFontStyle st)
        {
            return this.HtmlContainer.Adapter.GetFont(fontFamily, fsize, st);
        }

        protected override RColor GetActualColor(string colorStr)
        {
            return this.HtmlContainer.CssParser.ParseColor(colorStr);
        }

        protected override RPoint GetActualLocation(string X, string Y)
        {
            var left = CssValueParser.ParseLength(X, this.HtmlContainer.PageSize.Width, this, null);
            var top = CssValueParser.ParseLength(Y, this.HtmlContainer.PageSize.Height, this, null);
            return new RPoint(left, top);
        }

        /// <summary>
        /// ToString override.
        /// </summary>
        /// <returns></returns>
        public override string ToString()
        {
            var tag = this.HtmlTag != null ? string.Format("<{0}>", this.HtmlTag.Name) : "anon";

            if (this.IsBlock)
            {
                return string.Format("{0}{1} Block {2}, Children:{3}", this.ParentBox == null ? "Root: " : string.Empty, tag, this.FontSize, this.Boxes.Count);
            }
            else if (this.Display == CssConstants.None)
            {
                return string.Format("{0}{1} None", this.ParentBox == null ? "Root: " : string.Empty, tag);
            }
            else
            {
                return string.Format("{0}{1} {2}: {3}", this.ParentBox == null ? "Root: " : string.Empty, tag, this.Display, this.Text);
            }
        }

        #endregion
    }
}