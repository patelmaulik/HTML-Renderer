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

namespace HtmlRendererCore.Core
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;

    using HtmlRendererCore.Adapters;
    using HtmlRendererCore.Adapters.Entities;
    using HtmlRendererCore.Core.Dom;
    using HtmlRendererCore.Core.Entities;
    using HtmlRendererCore.Core.Handlers;
    using HtmlRendererCore.Core.Parse;
    using HtmlRendererCore.Core.Utils;

    /// <summary>
    /// Low level handling of Html Renderer logic.<br/>
    /// Allows html layout and rendering without association to actual control, those allowing to handle html rendering on any graphics object.<br/>
    /// Using this class will require the client to handle all propagation's of mouse/keyboard events, layout/paint calls, scrolling offset, 
    /// location/size/rectangle handling and UI refresh requests.<br/>
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>MaxSize and ActualSize:</b><br/>
    /// The max width and height of the rendered html.<br/>
    /// The max width will effect the html layout wrapping lines, resize images and tables where possible.<br/>
    /// The max height does NOT effect layout, but will not render outside it (clip).<br/>
    /// <see cref="ActualSize"/> can exceed the max size by layout restrictions (unwrap-able line, set image size, etc.).<br/>
    /// Set zero for unlimited (width/height separately).<br/>
    /// </para>
    /// <para>
    /// <b>ScrollOffset:</b><br/>
    /// This will adjust the rendered html by the given offset so the content will be "scrolled".<br/>
    /// Element that is rendered at location (50,100) with offset of (0,200) will not be rendered 
    /// at -100, therefore outside the client rectangle.
    /// </para>
    /// <para>
    /// <b>LinkClicked event</b><br/>
    /// Raised when the user clicks on a link in the html.<br/>
    /// Allows canceling the execution of the link to overwrite by custom logic.<br/>
    /// If error occurred in event handler it will propagate up the stack.
    /// </para>
    /// <para>
    /// <b>StylesheetLoad event:</b><br/>
    /// Raised when a stylesheet is about to be loaded by file path or URL in 'link' element.<br/>
    /// Allows to overwrite the loaded stylesheet by providing the stylesheet data manually, or different source (file or URL) to load from.<br/>
    /// Example: The stylesheet 'href' can be non-valid URI string that is interpreted in the overwrite delegate by custom logic to pre-loaded stylesheet object<br/>
    /// If no alternative data is provided the original source will be used.<br/>
    /// </para>
    /// <para>
    /// <b>ImageLoad event:</b><br/>
    /// Raised when an image is about to be loaded by file path, URL or inline data in 'img' element or background-image CSS style.<br/>
    /// Allows to overwrite the loaded image by providing the image object manually, or different source (file or URL) to load from.<br/>
    /// Example: image 'src' can be non-valid string that is interpreted in the overwrite delegate by custom logic to resource image object<br/>
    /// Example: image 'src' in the html is relative - the overwrite intercepts the load and provide full source URL to load the image from<br/>
    /// Example: image download requires authentication - the overwrite intercepts the load, downloads the image to disk using custom code and provide 
    /// file path to load the image from.<br/>
    /// If no alternative data is provided the original source will be used.<br/>
    /// </para>
    /// <para>
    /// <b>Refresh event:</b><br/>
    /// Raised when html renderer requires refresh of the control hosting (invalidation and re-layout).<br/>
    /// There is no guarantee that the event will be raised on the main thread, it can be raised on thread-pool thread.
    /// </para>
    /// <para>
    /// <b>RenderError event:</b><br/>
    /// Raised when an error occurred during html rendering.<br/>
    /// </para>
    /// </remarks>
    public sealed class HtmlContainerInt : IDisposable
    {
        #region Fields and Consts

        /// <summary>
        /// Main adapter to framework specific logic.
        /// </summary>
        private readonly RAdapter _adapter;

        /// <summary>
        /// parser for CSS data
        /// </summary>
        private readonly CssParser _cssParser;

        /// <summary>
        /// the root css box of the parsed html
        /// </summary>
        private CssBox _root;

        /// <summary>
        /// list of all css boxes that have ":hover" selector on them
        /// </summary>
        private List<HoverBoxBlock> _hoverBoxes;

        /// <summary>
        /// Handler for text selection in the html. 
        /// </summary>
        private SelectionHandler _selectionHandler;

        /// <summary>
        /// Handler for downloading of images in the html
        /// </summary>
        private ImageDownloader _imageDownloader;

        /// <summary>
        /// the text fore color use for selected text
        /// </summary>
        private RColor _selectionForeColor;

        /// <summary>
        /// the back-color to use for selected text
        /// </summary>
        private RColor _selectionBackColor;

        /// <summary>
        /// the parsed stylesheet data used for handling the html
        /// </summary>
        private CssData _cssData;

        /// <summary>
        /// Is content selection is enabled for the rendered html (default - true).<br/>
        /// If set to 'false' the rendered html will be static only with ability to click on links.
        /// </summary>
        private bool _isSelectionEnabled = true;

        /// <summary>
        /// Is the build-in context menu enabled (default - true)
        /// </summary>
        private bool _isContextMenuEnabled = true;

        /// <summary>
        /// Gets or sets a value indicating if anti-aliasing should be avoided 
        /// for geometry like backgrounds and borders
        /// </summary>
        private bool _avoidGeometryAntialias;

        /// <summary>
        /// Gets or sets a value indicating if image asynchronous loading should be avoided (default - false).<br/>
        /// </summary>
        private bool _avoidAsyncImagesLoading;

        /// <summary>
        /// Gets or sets a value indicating if image loading only when visible should be avoided (default - false).<br/>
        /// </summary>
        private bool _avoidImagesLateLoading;

        /// <summary>
        /// is the load of the html document is complete
        /// </summary>
        private bool _loadComplete;

        /// <summary>
        /// the top-left most location of the rendered html
        /// </summary>
        private RPoint _location;

        /// <summary>
        /// the max width and height of the rendered html, effects layout, actual size cannot exceed this values.<br/>
        /// Set zero for unlimited.<br/>
        /// </summary>
        private RSize _maxSize;

        /// <summary>
        /// Gets or sets the scroll offset of the document for scroll controls
        /// </summary>
        private RPoint _scrollOffset;

        /// <summary>
        /// The actual size of the rendered html (after layout)
        /// </summary>
        private RSize _actualSize;

        /// <summary>
        /// the top margin between the page start and the text
        /// </summary>
        private int _marginTop;

        /// <summary>
        /// the bottom margin between the page end and the text
        /// </summary>
        private int _marginBottom;

        /// <summary>
        /// the left margin between the page start and the text
        /// </summary>
        private int _marginLeft;

        /// <summary>
        /// the right margin between the page end and the text
        /// </summary>
        private int _marginRight;

        #endregion


        /// <summary>
        /// Init.
        /// </summary>
        public HtmlContainerInt(RAdapter adapter)
        {
            ArgChecker.AssertArgNotNull(adapter, "global");

            this._adapter = adapter;
            this._cssParser = new CssParser(adapter);
        }

        /// <summary>
        /// 
        /// </summary>
        internal RAdapter Adapter
        {
            get { return this._adapter; }
        }

        /// <summary>
        /// parser for CSS data
        /// </summary>
        internal CssParser CssParser
        {
            get { return this._cssParser; }
        }

        /// <summary>
        /// Raised when the set html document has been fully loaded.<br/>
        /// Allows manipulation of the html dom, scroll position, etc.
        /// </summary>
        public event EventHandler LoadComplete;

        /// <summary>
        /// Raised when the user clicks on a link in the html.<br/>
        /// Allows canceling the execution of the link.
        /// </summary>
        public event EventHandler<HtmlLinkClickedEventArgs> LinkClicked;

        /// <summary>
        /// Raised when html renderer requires refresh of the control hosting (invalidation and re-layout).
        /// </summary>
        /// <remarks>
        /// There is no guarantee that the event will be raised on the main thread, it can be raised on thread-pool thread.
        /// </remarks>
        public event EventHandler<HtmlRefreshEventArgs> Refresh;

        /// <summary>
        /// Raised when Html Renderer request scroll to specific location.<br/>
        /// This can occur on document anchor click.
        /// </summary>
        public event EventHandler<HtmlScrollEventArgs> ScrollChange;

        /// <summary>
        /// Raised when an error occurred during html rendering.<br/>
        /// </summary>
        /// <remarks>
        /// There is no guarantee that the event will be raised on the main thread, it can be raised on thread-pool thread.
        /// </remarks>
        public event EventHandler<HtmlRenderErrorEventArgs> RenderError;

        /// <summary>
        /// Raised when a stylesheet is about to be loaded by file path or URI by link element.<br/>
        /// This event allows to provide the stylesheet manually or provide new source (file or Uri) to load from.<br/>
        /// If no alternative data is provided the original source will be used.<br/>
        /// </summary>
        public event EventHandler<HtmlStylesheetLoadEventArgs> StylesheetLoad;

        /// <summary>
        /// Raised when an image is about to be loaded by file path or URI.<br/>
        /// This event allows to provide the image manually, if not handled the image will be loaded from file or download from URI.
        /// </summary>
        public event EventHandler<HtmlImageLoadEventArgs> ImageLoad;

        /// <summary>
        /// the parsed stylesheet data used for handling the html
        /// </summary>
        public CssData CssData
        {
            get { return this._cssData; }
        }

        /// <summary>
        /// Gets or sets a value indicating if anti-aliasing should be avoided for geometry like backgrounds and borders (default - false).
        /// </summary>
        public bool AvoidGeometryAntialias
        {
            get { return this._avoidGeometryAntialias; }
            set { this._avoidGeometryAntialias = value; }
        }

        /// <summary>
        /// Gets or sets a value indicating if image asynchronous loading should be avoided (default - false).<br/>
        /// True - images are loaded synchronously during html parsing.<br/>
        /// False - images are loaded asynchronously to html parsing when downloaded from URL or loaded from disk.<br/>
        /// </summary>
        /// <remarks>
        /// Asynchronously image loading allows to unblock html rendering while image is downloaded or loaded from disk using IO 
        /// ports to achieve better performance.<br/>
        /// Asynchronously image loading should be avoided when the full html content must be available during render, like render to image.
        /// </remarks>
        public bool AvoidAsyncImagesLoading
        {
            get { return this._avoidAsyncImagesLoading; }
            set { this._avoidAsyncImagesLoading = value; }
        }

        /// <summary>
        /// Gets or sets a value indicating if image loading only when visible should be avoided (default - false).<br/>
        /// True - images are loaded as soon as the html is parsed.<br/>
        /// False - images that are not visible because of scroll location are not loaded until they are scrolled to.
        /// </summary>
        /// <remarks>
        /// Images late loading improve performance if the page contains image outside the visible scroll area, especially if there is large 
        /// amount of images, as all image loading is delayed (downloading and loading into memory).<br/>
        /// Late image loading may effect the layout and actual size as image without set size will not have actual size until they are loaded
        /// resulting in layout change during user scroll.<br/>
        /// Early image loading may also effect the layout if image without known size above the current scroll location are loaded as they
        /// will push the html elements down.
        /// </remarks>
        public bool AvoidImagesLateLoading
        {
            get { return this._avoidImagesLateLoading; }
            set { this._avoidImagesLateLoading = value; }
        }

        /// <summary>
        /// Is content selection is enabled for the rendered html (default - true).<br/>
        /// If set to 'false' the rendered html will be static only with ability to click on links.
        /// </summary>
        public bool IsSelectionEnabled
        {
            get { return this._isSelectionEnabled; }
            set { this._isSelectionEnabled = value; }
        }

        /// <summary>
        /// Is the build-in context menu enabled and will be shown on mouse right click (default - true)
        /// </summary>
        public bool IsContextMenuEnabled
        {
            get { return this._isContextMenuEnabled; }
            set { this._isContextMenuEnabled = value; }
        }

        /// <summary>
        /// The scroll offset of the html.<br/>
        /// This will adjust the rendered html by the given offset so the content will be "scrolled".<br/>
        /// </summary>
        /// <example>
        /// Element that is rendered at location (50,100) with offset of (0,200) will not be rendered as it
        /// will be at -100 therefore outside the client rectangle.
        /// </example>
        public RPoint ScrollOffset
        {
            get { return this._scrollOffset; }
            set { this._scrollOffset = value; }
        }

        /// <summary>
        /// The top-left most location of the rendered html.<br/>
        /// This will offset the top-left corner of the rendered html.
        /// </summary>
        public RPoint Location
        {
            get { return this._location; }
            set { this._location = value; }
        }

        /// <summary>
        /// The max width and height of the rendered html.<br/>
        /// The max width will effect the html layout wrapping lines, resize images and tables where possible.<br/>
        /// The max height does NOT effect layout, but will not render outside it (clip).<br/>
        /// <see cref="ActualSize"/> can be exceed the max size by layout restrictions (unwrapable line, set image size, etc.).<br/>
        /// Set zero for unlimited (width\height separately).<br/>
        /// </summary>
        public RSize MaxSize
        {
            get { return this._maxSize; }
            set { this._maxSize = value; }
        }

        /// <summary>
        /// The actual size of the rendered html (after layout)
        /// </summary>
        public RSize ActualSize
        {
            get { return this._actualSize; }
            set { this._actualSize = value; }
        }

        public RSize PageSize { get; set; }

        /// <summary>
        /// the top margin between the page start and the text
        /// </summary>
        public int MarginTop
        {
            get { return this._marginTop; }
            set
            {
                if (value > -1)
                    this._marginTop = value;
            }
        }

        /// <summary>
        /// the bottom margin between the page end and the text
        /// </summary>
        public int MarginBottom
        {
            get { return this._marginBottom; }
            set
            {
                if (value > -1)
                    this._marginBottom = value;
            }
        }

        /// <summary>
        /// the left margin between the page start and the text
        /// </summary>
        public int MarginLeft
        {
            get { return this._marginLeft; }
            set
            {
                if (value > -1)
                    this._marginLeft = value;
            }
        }

        /// <summary>
        /// the right margin between the page end and the text
        /// </summary>
        public int MarginRight
        {
            get { return this._marginRight; }
            set
            {
                if (value > -1)
                    this._marginRight = value;
            }
        }

        /// <summary>
        /// Set all 4 margins to the given value.
        /// </summary>
        /// <param name="value"></param>
        public void SetMargins(int value)
        {
            if (value > -1)
                this._marginBottom = this._marginLeft = this._marginTop = this._marginRight = value;
        }

        /// <summary>
        /// Get the currently selected text segment in the html.
        /// </summary>
        public string SelectedText
        {
            get { return this._selectionHandler.GetSelectedText(); }
        }

        /// <summary>
        /// Copy the currently selected html segment with style.
        /// </summary>
        public string SelectedHtml
        {
            get { return this._selectionHandler.GetSelectedHtml(); }
        }

        /// <summary>
        /// the root css box of the parsed html
        /// </summary>
        internal CssBox Root
        {
            get { return this._root; }
        }

        /// <summary>
        /// the text fore color use for selected text
        /// </summary>
        internal RColor SelectionForeColor
        {
            get { return this._selectionForeColor; }
            set { this._selectionForeColor = value; }
        }

        /// <summary>
        /// the back-color to use for selected text
        /// </summary>
        internal RColor SelectionBackColor
        {
            get { return this._selectionBackColor; }
            set { this._selectionBackColor = value; }
        }

        /// <summary>
        /// Init with optional document and stylesheet.
        /// </summary>
        /// <param name="htmlSource">the html to init with, init empty if not given</param>
        /// <param name="baseCssData">optional: the stylesheet to init with, init default if not given</param>
        public void SetHtml(string htmlSource, CssData baseCssData = null)
        {
            this.Clear();
            if (!string.IsNullOrEmpty(htmlSource))
            {
                this._loadComplete = false;
                this._cssData = baseCssData ?? this._adapter.DefaultCssData;

                DomParser parser = new DomParser(this._cssParser);
                this._root = parser.GenerateCssTree(htmlSource, this, ref this._cssData);
                if (this._root != null)
                {
                    this._selectionHandler = new SelectionHandler(this._root);
                    this._imageDownloader = new ImageDownloader();
                }
            }
        }

        /// <summary>
        /// Clear the content of the HTML container releasing any resources used to render previously existing content.
        /// </summary>
        public void Clear()
        {
            if (this._root != null)
            {
                this._root.Dispose();
                this._root = null;

                if (this._selectionHandler != null)
                    this._selectionHandler.Dispose();
                this._selectionHandler = null;

                if (this._imageDownloader != null)
                    this._imageDownloader.Dispose();
                this._imageDownloader = null;

                this._hoverBoxes = null;
            }
        }

        /// <summary>
        /// Clear the current selection.
        /// </summary>
        public void ClearSelection()
        {
            if (this._selectionHandler != null)
            {
                this._selectionHandler.ClearSelection();
                this.RequestRefresh(false);
            }
        }

        /// <summary>
        /// Get html from the current DOM tree with style if requested.
        /// </summary>
        /// <param name="styleGen">Optional: controls the way styles are generated when html is generated (default: <see cref="HtmlGenerationStyle.Inline"/>)</param>
        /// <returns>generated html</returns>
        public string GetHtml(HtmlGenerationStyle styleGen = HtmlGenerationStyle.Inline)
        {
            return DomUtils.GenerateHtml(this._root, styleGen);
        }

        /// <summary>
        /// Get attribute value of element at the given x,y location by given key.<br/>
        /// If more than one element exist with the attribute at the location the inner most is returned.
        /// </summary>
        /// <param name="location">the location to find the attribute at</param>
        /// <param name="attribute">the attribute key to get value by</param>
        /// <returns>found attribute value or null if not found</returns>
        public string GetAttributeAt(RPoint location, string attribute)
        {
            ArgChecker.AssertArgNotNullOrEmpty(attribute, "attribute");

            var cssBox = DomUtils.GetCssBox(this._root, this.OffsetByScroll(location));
            return cssBox != null ? DomUtils.GetAttribute(cssBox, attribute) : null;
        }

        /// <summary>
        /// Get all the links in the HTML with the element rectangle and href data.
        /// </summary>
        /// <returns>collection of all the links in the HTML</returns>
        public List<LinkElementData<RRect>> GetLinks()
        {
            var linkBoxes = new List<CssBox>();
            DomUtils.GetAllLinkBoxes(this._root, linkBoxes);

            var linkElements = new List<LinkElementData<RRect>>();
            foreach (var box in linkBoxes)
            {
                linkElements.Add(new LinkElementData<RRect>(box.GetAttribute("id"), box.GetAttribute("href"), CommonUtils.GetFirstValueOrDefault(box.Rectangles, box.Bounds)));
            }
            return linkElements;
        }

        /// <summary>
        /// Get css link href at the given x,y location.
        /// </summary>
        /// <param name="location">the location to find the link at</param>
        /// <returns>css link href if exists or null</returns>
        public string GetLinkAt(RPoint location)
        {
            var link = DomUtils.GetLinkBox(this._root, this.OffsetByScroll(location));
            return link != null ? link.HrefLink : null;
        }

        /// <summary>
        /// Get the rectangle of html element as calculated by html layout.<br/>
        /// Element if found by id (id attribute on the html element).<br/>
        /// Note: to get the screen rectangle you need to adjust by the hosting control.<br/>
        /// </summary>
        /// <param name="elementId">the id of the element to get its rectangle</param>
        /// <returns>the rectangle of the element or null if not found</returns>
        public RRect? GetElementRectangle(string elementId)
        {
            ArgChecker.AssertArgNotNullOrEmpty(elementId, "elementId");

            var box = DomUtils.GetBoxById(this._root, elementId.ToLower());
            return box != null ? CommonUtils.GetFirstValueOrDefault(box.Rectangles, box.Bounds) : (RRect?)null;
        }

        /// <summary>
        /// Measures the bounds of box and children, recursively.
        /// </summary>
        /// <param name="g">Device context to draw</param>
        public void PerformLayout(RGraphics g)
        {
            ArgChecker.AssertArgNotNull(g, "g");

            this._actualSize = RSize.Empty;
            if (this._root != null)
            {
                // if width is not restricted we set it to large value to get the actual later
                this._root.Size = new RSize(this._maxSize.Width > 0 ? this._maxSize.Width : 99999, 0);
                this._root.Location = this._location;
                this._root.PerformLayout(g);

                if (this._maxSize.Width <= 0.1)
                {
                    // in case the width is not restricted we need to double layout, first will find the width so second can layout by it (center alignment)
                    this._root.Size = new RSize((int)Math.Ceiling(this._actualSize.Width), 0);
                    this._actualSize = RSize.Empty;
                    this._root.PerformLayout(g);
                }

                if (!this._loadComplete)
                {
                    this._loadComplete = true;
                    EventHandler handler = this.LoadComplete;
                    if (handler != null)
                        handler(this, EventArgs.Empty);
                }
            }
        }

        /// <summary>
        /// Render the html using the given device.
        /// </summary>
        /// <param name="g">the device to use to render</param>
        public void PerformPaint(RGraphics g)
        {
            ArgChecker.AssertArgNotNull(g, "g");

            if (this.MaxSize.Height > 0)
            {
                g.PushClip(new RRect(this._location.X, this._location.Y, Math.Min(this._maxSize.Width, this.PageSize.Width), Math.Min(this._maxSize.Height, this.PageSize.Height)));
            }
            else
            {
                g.PushClip(new RRect(this.MarginLeft, this.MarginTop, this.PageSize.Width, this.PageSize.Height));
            }

            if (this._root != null)
            {
                this._root.Paint(g);
            }

            g.PopClip();
        }

        /// <summary>
        /// Handle mouse down to handle selection.
        /// </summary>
        /// <param name="parent">the control hosting the html to invalidate</param>
        /// <param name="location">the location of the mouse</param>
        public void HandleMouseDown(RControl parent, RPoint location)
        {
            ArgChecker.AssertArgNotNull(parent, "parent");

            try
            {
                if (this._selectionHandler != null)
                    this._selectionHandler.HandleMouseDown(parent, this.OffsetByScroll(location), this.IsMouseInContainer(location));
            }
            catch (Exception ex)
            {
                this.ReportError(HtmlRenderErrorType.KeyboardMouse, "Failed mouse down handle", ex);
            }
        }

        /// <summary>
        /// Handle mouse up to handle selection and link click.
        /// </summary>
        /// <param name="parent">the control hosting the html to invalidate</param>
        /// <param name="location">the location of the mouse</param>
        /// <param name="e">the mouse event data</param>
        public void HandleMouseUp(RControl parent, RPoint location, RMouseEvent e)
        {
            ArgChecker.AssertArgNotNull(parent, "parent");

            try
            {
                if (this._selectionHandler != null && this.IsMouseInContainer(location))
                {
                    var ignore = this._selectionHandler.HandleMouseUp(parent, e.LeftButton);
                    if (!ignore && e.LeftButton)
                    {
                        var loc = this.OffsetByScroll(location);
                        var link = DomUtils.GetLinkBox(this._root, loc);
                        if (link != null)
                        {
                            this.HandleLinkClicked(parent, location, link);
                        }
                    }
                }
            }
            catch (HtmlLinkClickedException)
            {
                throw;
            }
            catch (Exception ex)
            {
                this.ReportError(HtmlRenderErrorType.KeyboardMouse, "Failed mouse up handle", ex);
            }
        }

        /// <summary>
        /// Handle mouse double click to select word under the mouse.
        /// </summary>
        /// <param name="parent">the control hosting the html to set cursor and invalidate</param>
        /// <param name="location">the location of the mouse</param>
        public void HandleMouseDoubleClick(RControl parent, RPoint location)
        {
            ArgChecker.AssertArgNotNull(parent, "parent");

            try
            {
                if (this._selectionHandler != null && this.IsMouseInContainer(location))
                    this._selectionHandler.SelectWord(parent, this.OffsetByScroll(location));
            }
            catch (Exception ex)
            {
                this.ReportError(HtmlRenderErrorType.KeyboardMouse, "Failed mouse double click handle", ex);
            }
        }

        /// <summary>
        /// Handle mouse move to handle hover cursor and text selection.
        /// </summary>
        /// <param name="parent">the control hosting the html to set cursor and invalidate</param>
        /// <param name="location">the location of the mouse</param>
        public void HandleMouseMove(RControl parent, RPoint location)
        {
            ArgChecker.AssertArgNotNull(parent, "parent");

            try
            {
                var loc = this.OffsetByScroll(location);
                if (this._selectionHandler != null && this.IsMouseInContainer(location))
                    this._selectionHandler.HandleMouseMove(parent, loc);

                /*
                if( _hoverBoxes != null )
                {
                    bool refresh = false;
                    foreach(var hoverBox in _hoverBoxes)
                    {
                        foreach(var rect in hoverBox.Item1.Rectangles.Values)
                        {
                            if( rect.Contains(loc) )
                            {
                                //hoverBox.Item1.Color = "gold";
                                refresh = true;
                            }
                        }
                    }

                    if(refresh)
                        RequestRefresh(true);
                }
                 */
            }
            catch (Exception ex)
            {
                this.ReportError(HtmlRenderErrorType.KeyboardMouse, "Failed mouse move handle", ex);
            }
        }

        /// <summary>
        /// Handle mouse leave to handle hover cursor.
        /// </summary>
        /// <param name="parent">the control hosting the html to set cursor and invalidate</param>
        public void HandleMouseLeave(RControl parent)
        {
            ArgChecker.AssertArgNotNull(parent, "parent");

            try
            {
                if (this._selectionHandler != null)
                    this._selectionHandler.HandleMouseLeave(parent);
            }
            catch (Exception ex)
            {
                this.ReportError(HtmlRenderErrorType.KeyboardMouse, "Failed mouse leave handle", ex);
            }
        }

        /// <summary>
        /// Handle key down event for selection and copy.
        /// </summary>
        /// <param name="parent">the control hosting the html to invalidate</param>
        /// <param name="e">the pressed key</param>
        public void HandleKeyDown(RControl parent, RKeyEvent e)
        {
            ArgChecker.AssertArgNotNull(parent, "parent");
            ArgChecker.AssertArgNotNull(e, "e");

            try
            {
                if (e.Control && this._selectionHandler != null)
                {
                    // select all
                    if (e.AKeyCode)
                    {
                        this._selectionHandler.SelectAll(parent);
                    }

                    // copy currently selected text
                    if (e.CKeyCode)
                    {
                        this._selectionHandler.CopySelectedHtml();
                    }
                }
            }
            catch (Exception ex)
            {
                this.ReportError(HtmlRenderErrorType.KeyboardMouse, "Failed key down handle", ex);
            }
        }

        /// <summary>
        /// Raise the stylesheet load event with the given event args.
        /// </summary>
        /// <param name="args">the event args</param>
        internal void RaiseHtmlStylesheetLoadEvent(HtmlStylesheetLoadEventArgs args)
        {
            try
            {
                EventHandler<HtmlStylesheetLoadEventArgs> handler = this.StylesheetLoad;
                if (handler != null)
                    handler(this, args);
            }
            catch (Exception ex)
            {
                this.ReportError(HtmlRenderErrorType.CssParsing, "Failed stylesheet load event", ex);
            }
        }

        /// <summary>
        /// Raise the image load event with the given event args.
        /// </summary>
        /// <param name="args">the event args</param>
        internal void RaiseHtmlImageLoadEvent(HtmlImageLoadEventArgs args)
        {
            try
            {
                EventHandler<HtmlImageLoadEventArgs> handler = this.ImageLoad;
                if (handler != null)
                    handler(this, args);
            }
            catch (Exception ex)
            {
                this.ReportError(HtmlRenderErrorType.Image, "Failed image load event", ex);
            }
        }

        /// <summary>
        /// Request invalidation and re-layout of the control hosting the renderer.
        /// </summary>
        /// <param name="layout">is re-layout is required for the refresh</param>
        public void RequestRefresh(bool layout)
        {
            try
            {
                EventHandler<HtmlRefreshEventArgs> handler = this.Refresh;
                if (handler != null)
                    handler(this, new HtmlRefreshEventArgs(layout));
            }
            catch (Exception ex)
            {
                this.ReportError(HtmlRenderErrorType.General, "Failed refresh request", ex);
            }
        }

        /// <summary>
        /// Report error in html render process.
        /// </summary>
        /// <param name="type">the type of error to report</param>
        /// <param name="message">the error message</param>
        /// <param name="exception">optional: the exception that occured</param>
        internal void ReportError(HtmlRenderErrorType type, string message, Exception exception = null)
        {
            try
            {
                EventHandler<HtmlRenderErrorEventArgs> handler = this.RenderError;
                if (handler != null)
                    handler(this, new HtmlRenderErrorEventArgs(type, message, exception));
            }
            catch
            { }
        }

        /// <summary>
        /// Handle link clicked going over <see cref="LinkClicked"/> event and using <see cref="Process.Start()"/> if not canceled.
        /// </summary>
        /// <param name="parent">the control hosting the html to invalidate</param>
        /// <param name="location">the location of the mouse</param>
        /// <param name="link">the link that was clicked</param>
        internal void HandleLinkClicked(RControl parent, RPoint location, CssBox link)
        {
            EventHandler<HtmlLinkClickedEventArgs> clickHandler = this.LinkClicked;
            if (clickHandler != null)
            {
                var args = new HtmlLinkClickedEventArgs(link.HrefLink, link.HtmlTag.Attributes);
                try
                {
                    clickHandler(this, args);
                }
                catch (Exception ex)
                {
                    throw new HtmlLinkClickedException("Error in link clicked intercept", ex);
                }
                if (args.Handled)
                    return;
            }

            if (!string.IsNullOrEmpty(link.HrefLink))
            {
                if (link.HrefLink.StartsWith("#") && link.HrefLink.Length > 1)
                {
                    EventHandler<HtmlScrollEventArgs> scrollHandler = this.ScrollChange;
                    if (scrollHandler != null)
                    {
                        var rect = this.GetElementRectangle(link.HrefLink.Substring(1));
                        if (rect.HasValue)
                        {
                            scrollHandler(this, new HtmlScrollEventArgs(rect.Value.Location));
                            this.HandleMouseMove(parent, location);
                        }
                    }
                }
                else
                {
                    var nfo = new ProcessStartInfo(link.HrefLink);
                    nfo.UseShellExecute = true;
                    Process.Start(nfo);
                }
            }
        }

        /// <summary>
        /// Add css box that has ":hover" selector to be handled on mouse hover.
        /// </summary>
        /// <param name="box">the box that has the hover selector</param>
        /// <param name="block">the css block with the css data with the selector</param>
        internal void AddHoverBox(CssBox box, CssBlock block)
        {
            ArgChecker.AssertArgNotNull(box, "box");
            ArgChecker.AssertArgNotNull(block, "block");

            if (this._hoverBoxes == null)
                this._hoverBoxes = new List<HoverBoxBlock>();

            this._hoverBoxes.Add(new HoverBoxBlock(box, block));
        }

        /// <summary>
        /// Get image downloader to be used to download images for the current html rendering.<br/>
        /// Lazy create single downloader to be used for all images in the current html.
        /// </summary>
        internal ImageDownloader GetImageDownloader()
        {
            return this._imageDownloader;
        }

        /// <summary>
        /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
        /// </summary>
        /// <filterpriority>2</filterpriority>
        public void Dispose()
        {
            this.Dispose(true);
        }


        #region Private methods

        /// <summary>
        /// Adjust the offset of the given location by the current scroll offset.
        /// </summary>
        /// <param name="location">the location to adjust</param>
        /// <returns>the adjusted location</returns>
        private RPoint OffsetByScroll(RPoint location)
        {
            return new RPoint(location.X - this.ScrollOffset.X, location.Y - this.ScrollOffset.Y);
        }

        /// <summary>
        /// Check if the mouse is currently on the html container.<br/>
        /// Relevant if the html container is not filled in the hosted control (location is not zero and the size is not the full size of the control).
        /// </summary>
        private bool IsMouseInContainer(RPoint location)
        {
            return location.X >= this._location.X && location.X <= this._location.X + this._actualSize.Width && location.Y >= this._location.Y + this.ScrollOffset.Y && location.Y <= this._location.Y + this.ScrollOffset.Y + this._actualSize.Height;
        }

        /// <summary>
        /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
        /// </summary>
        private void Dispose(bool all)
        {
            try
            {
                if (all)
                {
                    this.LinkClicked = null;
                    this.Refresh = null;
                    this.RenderError = null;
                    this.StylesheetLoad = null;
                    this.ImageLoad = null;
                }

                this._cssData = null;
                if (this._root != null)
                    this._root.Dispose();
                this._root = null;
                if (this._selectionHandler != null)
                    this._selectionHandler.Dispose();
                this._selectionHandler = null;
            }
            catch
            { }
        }

        #endregion
    }
}