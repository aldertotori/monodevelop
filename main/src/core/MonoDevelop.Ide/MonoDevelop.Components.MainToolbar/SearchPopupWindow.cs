// 
// SearchPopupWindow.cs
//  
// Author:
//       Mike Krüger <mkrueger@xamarin.com>
// 
// Copyright (c) 2012 Xamarin Inc. (http://xamarin.com)
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.
using System;
using System.Threading;
using System.Threading.Tasks;
using MonoDevelop.Core;
using System.Collections.Generic;
using Gtk;
using System.Linq;
using MonoDevelop.Ide;
using MonoDevelop.Ide.CodeCompletion;
using Mono.Addins;
using MonoDevelop.Ide.Gui;
using MonoDevelop.Core.Text;
using System.Collections.Immutable;

namespace MonoDevelop.Components.MainToolbar
{
	class SearchPopupWindow : PopoverWindow
	{
		const int yMargin = 0;
		const int xMargin = 6;
		const int itemSeparatorHeight = 2;
		const int marginIconSpacing = 4;
		const int iconTextSpacing = 6;
		const int categorySeparatorHeight = 8;
		const int headerMarginSize = 100;

		List<SearchCategory> categories = new List<SearchCategory> ();
		List<Tuple<SearchCategory, IReadOnlyList<SearchResult>>> results = new List<Tuple<SearchCategory, IReadOnlyList<SearchResult>>> ();
		List<Tuple<SearchCategory, IReadOnlyList<SearchResult>>> incompleteResults = new List<Tuple<SearchCategory, IReadOnlyList<SearchResult>>> ();
		Pango.Layout layout, headerLayout;
		CancellationTokenSource src;
		Cairo.Color headerColor;
		Cairo.Color separatorLine;
		Cairo.Color darkSearchBackground;
		Cairo.Color lightSearchBackground;

		Cairo.Color selectionBackgroundColor;

		bool isInSearch;
		class NullDataSource : ISearchDataSource
		{
			#region ISearchDataSource implementation
			Xwt.Drawing.Image ISearchDataSource.GetIcon (int item)
			{
				throw new NotImplementedException ();
			}
			string ISearchDataSource.GetMarkup (int item, bool isSelected)
			{
				throw new NotImplementedException ();
			}
			string ISearchDataSource.GetDescriptionMarkup (int item, bool isSelected)
			{
				throw new NotImplementedException ();
			}
			Task<TooltipInformation> ISearchDataSource.GetTooltip (CancellationToken token, int item)
			{
				throw new NotImplementedException ();
			}
			double ISearchDataSource.GetWeight (int item)
			{
				throw new NotImplementedException ();
			}
			
			ISegment ISearchDataSource.GetRegion (int item)
			{
				throw new NotImplementedException ();
			}
			
			string ISearchDataSource.GetFileName (int item)
			{
				throw new NotImplementedException ();
			}
			bool ISearchDataSource.CanActivate (int item)
			{
				throw new NotImplementedException ();
			}
			void ISearchDataSource.Activate (int item)
			{
				throw new NotImplementedException ();
			}
			int ISearchDataSource.ItemCount {
				get {
					return 0;
				}
			}
			#endregion
		}
		public SearchPopupWindow ()
		{
			headerColor = CairoExtensions.ParseColor ("8c8c8c");
			separatorLine = CairoExtensions.ParseColor ("dedede");
			lightSearchBackground = CairoExtensions.ParseColor ("ffffff");
			darkSearchBackground = CairoExtensions.ParseColor ("f7f7f7");
			selectionBackgroundColor = CairoExtensions.ParseColor ("cccccc");
			TypeHint = Gdk.WindowTypeHint.Combo;
			this.SkipTaskbarHint = true;
			this.SkipPagerHint = true;
			this.TransientFor = IdeApp.Workbench.RootWindow;
			this.AllowShrink = false;
			this.AllowGrow = false;

			categories.Add (new FileSearchCategory (this));
			categories.Add (new CommandSearchCategory (this));
			categories.Add (new SearchInSolutionSearchCategory ());
			foreach (var cat in AddinManager.GetExtensionObjects<SearchCategory> ("/MonoDevelop/Ide/SearchCategories")) {
				categories.Add (cat);
				cat.Initialize (this);
			}

			categories.Sort ();

			layout = new Pango.Layout (PangoContext);
			headerLayout = new Pango.Layout (PangoContext);

			layout.Ellipsize = Pango.EllipsizeMode.End;
			headerLayout.Ellipsize = Pango.EllipsizeMode.End;

			Events = Gdk.EventMask.ButtonPressMask | Gdk.EventMask.ButtonMotionMask | Gdk.EventMask.ButtonReleaseMask | Gdk.EventMask.ExposureMask | Gdk.EventMask.PointerMotionMask;
			ItemActivated += (sender, e) => OpenFile ();
			/*
			SizeRequested += delegate(object o, SizeRequestedArgs args) {
				if (inResize)
					return;
				if (args.Requisition.Width != Allocation.Width || args.Requisition.Height != Allocation.Height) {
					inResize = true;
//					Visible = false;
					Resize (args.Requisition.Width, args.Requisition.Height);
//					Visible = true;
					if (!Visible)
						Visible = true;
					inResize = false;
				}
			};*/
		}

		bool inResize = false;

		public bool SearchForMembers {
			get;
			set;
		}

		protected override void OnDestroyed ()
		{
			if (src != null)
				src.Cancel ();
			HideTooltip ();
			this.declarationviewwindow.Destroy ();
			base.OnDestroyed ();
		}

		internal void OpenFile ()
		{
			if (selectedItem == null || selectedItem.Item < 0 || selectedItem.Item >= selectedItem.DataSource.Count)
				return;

			if (selectedItem.DataSource[selectedItem.Item].CanActivate) {
				Destroy ();
				selectedItem.DataSource[selectedItem.Item].Activate ();
			}
			else {
				var region = SelectedItemRegion;
				Destroy ();

				if (string.IsNullOrEmpty (SelectedItemFileName))
					return;
				if (region.Length <= 0) {
					if (Pattern.LineNumber == 0) {
						IdeApp.Workbench.OpenDocument (SelectedItemFileName);
					} else {
						IdeApp.Workbench.OpenDocument (SelectedItemFileName, Pattern.LineNumber, Pattern.HasColumn ? Pattern.Column : 1);
					}
				} else {
					IdeApp.Workbench.OpenDocument (new FileOpenInformation (SelectedItemFileName, null) {
						Offset = region.Offset
					});
				}
			}
		}
		SearchPopupSearchPattern pattern;

		public SearchPopupSearchPattern Pattern {
			get {
				return pattern;
			}
		}
		static readonly SearchCategory.DataItemComparer cmp = new SearchCategory.DataItemComparer ();

		class SearchResultCollector : ISearchResultCallback, IDisposable
		{
			readonly SearchPopupWindow parent;
			ImmutableList<SearchResult> searchResults = ImmutableList<SearchResult>.Empty;
			uint timeout;

			public IReadOnlyList<SearchResult> Results {
				get {
					return searchResults;
				}	
			}

			public SearchCategory Category { get; private set;}

			public SearchResultCollector (SearchPopupWindow parent, SearchCategory cat)
			{
				this.parent = parent;
				this.Category = cat;
			}

			public Task Task { get; set; }

			#region ISearchResultCallback implementation
			void ISearchResultCallback.ReportResult (SearchResult result)
			{
				int i = Math.Min (maxItems, searchResults.Count);
				int lastCmp = 1;
				while (i > 0) {
					if ((lastCmp = cmp.Compare (result, searchResults [i - 1])) > 0)
						break;
					i--;
				}

				if (i < 0 || i >= maxItems && lastCmp <= 0)
					return;
				searchResults = searchResults.Insert (i, result);
				Application.Invoke (delegate {
					RemoveTimeout ();
					timeout = GLib.Timeout.Add (200, delegate {
						parent.ShowResult (Category, searchResults);
						parent.QueueResize ();
						timeout = 0;
						return false;
					});
				});
			}

			void RemoveTimeout ()
			{
				if (timeout == 0)
					return;
				GLib.Source.Remove (timeout);
				timeout = 0;
			}

			public void Dispose ()
			{
				RemoveTimeout ();
			}

			#endregion
		}

		public void Update (SearchPopupSearchPattern pattern)
		{
			// in case of 'string:' it's not clear if the user ment 'tag:pattern'  or 'pattern:line' therefore guess
			// 'tag:', if no valid tag is found guess 'pattern:'
			if (!string.IsNullOrEmpty (pattern.Tag) && string.IsNullOrEmpty (pattern.Pattern) && !categories.Any (c => c.IsValidTag (pattern.Tag))) {
				pattern = new SearchPopupSearchPattern (null, pattern.Tag, pattern.LineNumber, pattern.Column, pattern.UnparsedPattern);
			}

			this.pattern = pattern;
			if (src != null)
				src.Cancel ();

			HideTooltip ();
			src = new CancellationTokenSource ();
			isInSearch = true;
			if (results.Count == 0) {
				QueueDraw ();
			}
			incompleteResults.Clear ();
			var collectors = new List<SearchResultCollector> ();
			var token = src.Token;
			foreach (var _cat in categories) {
				var cat = _cat;
				var col = new SearchResultCollector (this, _cat);
				collectors.Add (col);
				col.Task = cat.GetResults (col, pattern, token);
			}

			Task.WhenAll (collectors.Select (c => c.Task)).ContinueWith (t => {
				if (t.IsCanceled)
					return;
				if (t.IsFaulted) {
					LoggingService.LogError ("Error getting search results", t.Exception);
				} else {
					Application.Invoke (delegate {
						if (token.IsCancellationRequested)
							return;
						foreach (var col in collectors) {
							ShowResult (col.Category, col.Results);
							col.Dispose ();
						}
						isInSearch = false;
						AnimatedResize ();
					});
				}
			}, token);
		}

		void ShowResult (SearchCategory cat, IReadOnlyList<SearchResult> result)
		{
			bool found = false;
			for (int i = 0; i < incompleteResults.Count; i++) {
				var ir = incompleteResults [i];
				if (ir.Item1 == cat) {
					incompleteResults[i] = Tuple.Create (cat, result);
					found = true;
					break;
				}
			}
			if (!found) {
				incompleteResults.Add (Tuple.Create (cat, result));
				incompleteResults.Sort ((x, y) => {
					return categories.IndexOf (x.Item1).CompareTo (categories.IndexOf (y.Item1));
				});
			}

			//if (incompleteResults.Count == categories.Count) 
			{
				results.Clear ();
				results.AddRange (incompleteResults);
				topItem = null;

				for (int i = 0; i < results.Count; i++) {
					var tuple = results [i];
					try {
						if (tuple.Item2.Count == 0)
							continue;
						if (topItem == null || topItem.DataSource[topItem.Item].Weight < tuple.Item2[0].Weight)
							topItem = new ItemIdentifier(tuple.Item1, tuple.Item2, 0);
					} catch (Exception e) {
						LoggingService.LogError ("Error while showing result " + i, e);
						continue;
					}
				}
				selectedItem = topItem;

				ShowTooltip ();

			}
		}

		int calculatedItems;
		Gdk.Size GetIdealSize ()
		{
			Gdk.Size retVal = new Gdk.Size ();
			int ox, oy;
			GetPosition (out ox, out oy);
			Gdk.Rectangle geometry = DesktopService.GetUsableMonitorGeometry (Screen, Screen.GetMonitorAtPoint (ox, oy));
			var maxHeight = geometry.Height * 4 / 5;
			double startY = yMargin + ChildAllocation.Y;
			double y = startY;
			calculatedItems = 0;
			foreach (var result in results) {
				var dataSrc = result.Item2;
				if (dataSrc.Count == 0)
					continue;
				
				for (int i = 0; i < maxItems && i < dataSrc.Count; i++) {
					layout.SetMarkup (GetRowMarkup (dataSrc[i]));
					int w, h;
					layout.GetPixelSize (out w, out h);
					if (y + h + itemSeparatorHeight > maxHeight)
						break;
					y += h + itemSeparatorHeight;
					calculatedItems++;
				}
			}
			retVal.Width = Math.Min (geometry.Width * 4 / 5, 480);
			if (Math.Abs (y - startY) < 1) {
				layout.SetMarkup (GettextCatalog.GetString ("No matches"));
				int w, h;
				layout.GetPixelSize (out w, out h);
				var realHeight = h + itemSeparatorHeight + 4;
				y += realHeight;
			} else {
				y -= itemSeparatorHeight;
			}

			var calculatedHeight = Math.Min (
				maxHeight, 
				(int)y + yMargin + results.Count (res => res.Item2.Count > 0) * categorySeparatorHeight
			);
			retVal.Height = calculatedHeight;
			return retVal;
		}

		const int maxItems = 8;

		protected override void OnSizeRequested (ref Requisition requisition)
		{
			base.OnSizeRequested (ref requisition);
			if (!inResize) {
				Gdk.Size idealSize = GetIdealSize ();
				requisition.Width = idealSize.Width;
				requisition.Height = idealSize.Height;
			}
		}

		ItemIdentifier GetItemAt (double px, double py)
		{
			double y = ChildAllocation.Y + yMargin;
			if (topItem != null){
				layout.SetMarkup (GetRowMarkup (topItem.DataSource[topItem.Item]));
				int w, h;
				layout.GetPixelSize (out w, out h);
				y += h + itemSeparatorHeight;
				if (y > py)
					return new ItemIdentifier (topItem.Category, topItem.DataSource, topItem.Item);
			}
			foreach (var result in results) {
				var category = result.Item1;
				var dataSrc = result.Item2;
				int itemsAdded = 0;
				for (int i = 0; i < maxItems && i < dataSrc.Count; i++) {
					if (topItem != null && topItem.DataSource == dataSrc && topItem.Item == i)
						continue;
					layout.SetMarkup (GetRowMarkup (dataSrc[i]));

					int w, h;
					layout.GetPixelSize (out w, out h);
					y += h + itemSeparatorHeight;
					if (y > py){
						return new ItemIdentifier (category, dataSrc, i);
					}

//					var region = dataSrc.GetRegion (i);
//					if (!region.IsEmpty) {
//						layout.SetMarkup (region.BeginLine.ToString ());
//						int w2, h2;
//						layout.GetPixelSize (out w2, out h2);
//						w += w2;
//					}
					itemsAdded++;
				}
				if (itemsAdded > 0)
					y += categorySeparatorHeight;
			}
			return null;
		}

		protected override bool OnMotionNotifyEvent (Gdk.EventMotion evnt)
		{
			var item = GetItemAt (evnt.X, evnt.Y);
			if (item == null && selectedItem != null || 
			    item != null && selectedItem == null || item != null && !item.Equals (selectedItem)) {
				selectedItem = item;
				ShowTooltip ();
				QueueDraw ();
			}
			return base.OnMotionNotifyEvent (evnt);
		}

		protected override bool OnButtonPressEvent (Gdk.EventButton evnt)
		{
			if (evnt.Button == 1) {
				if (selectedItem != null)
					OnItemActivated (EventArgs.Empty);
			}

			return base.OnButtonPressEvent (evnt);
		}

		int SelectedCategoryIndex {
			get {
				for (int i = 0; i < results.Count; i++) {
					if (results [i].Item1 == selectedItem.Category) {
						return i;
					}
				}
				return -1;
			}
		}

		void SelectItemUp ()
		{
			if (selectedItem == null || selectedItem == topItem)
				return;
			int i = SelectedCategoryIndex;
			if (selectedItem.Item > 0) {
				selectedItem = new ItemIdentifier (selectedItem.Category, selectedItem.DataSource, selectedItem.Item - 1);
				if (i > 0 && selectedItem.Equals (topItem)) {
					SelectItemUp ();
					return;
				}
			} else {
				if (i == 0) {
					selectedItem = topItem;
				} else {
					do {
						i--;
						selectedItem = new ItemIdentifier (
							results [i].Item1,
							results [i].Item2,
							Math.Min (maxItems, results [i].Item2.Count) - 1
						);
						if (selectedItem.Category == topItem.Category && selectedItem.Item == topItem.Item && i > 0) {
							i--;
							selectedItem = new ItemIdentifier (
								results [i].Item1,
								results [i].Item2,
								Math.Min (maxItems, results [i].Item2.Count) - 1
							);
						}
							
					} while (i > 0 && selectedItem.DataSource.Count <= 0);

					if (selectedItem.DataSource.Count <= 0) {
						selectedItem = topItem;
					}
				}
			}
			ShowTooltip ();
			QueueDraw ();
		}

		void SelectItemDown ()
		{
			if (selectedItem == null)
				return;

			if (selectedItem.Equals (topItem)) {
				for (int j = 0; j < results.Count; j++) {
					if (results[j].Item2.Count == 0 || results[j].Item2.Count == 1 && topItem.DataSource == results[j].Item2)
						continue;
					selectedItem = new ItemIdentifier (
						results [j].Item1,
						results [j].Item2,
						0
						);
					if (selectedItem.Equals (topItem))
						goto normalDown;
					break;
				}
				ShowTooltip ();
				QueueDraw ();
				return;
			}
		normalDown:
			var i = SelectedCategoryIndex;

			// check real upper bound
			if (selectedItem != null) {
				var curAbsoluteIndex = selectedItem == topItem ? 1 : 0;
				for (int j = 0; j < i; j++) {
					curAbsoluteIndex += Math.Min (maxItems, results [j].Item2.Count);
				}
				curAbsoluteIndex += selectedItem.Item + 1;
				if (curAbsoluteIndex + 1 > calculatedItems)
					return;
			}

			var upperBound = Math.Min (maxItems, selectedItem.DataSource.Count);
			if (selectedItem.Item + 1 < upperBound) {
				if (topItem.DataSource == selectedItem.DataSource && selectedItem.Item == upperBound - 1)
					return;
				selectedItem = new ItemIdentifier (selectedItem.Category, selectedItem.DataSource, selectedItem.Item + 1);
			} else {
				for (int j = i + 1; j < results.Count; j++) {
					if (results[j].Item2.Count == 0 || results[j].Item2.Count == 1 && topItem.DataSource == results[j].Item2)
						continue;
					selectedItem = new ItemIdentifier (
						results [j].Item1,
						results [j].Item2,
						0
						);
					if (selectedItem.Equals (topItem)) {
						selectedItem = new ItemIdentifier (
							results [j].Item1,
							results [j].Item2,
							1
							);
					}

					break;
				}
			}
			ShowTooltip ();
			QueueDraw ();
		}

		TooltipInformationWindow declarationviewwindow = new TooltipInformationWindow ();
		uint declarationViewTimer, declarationViewWindowOpacityTimer;
		void RemoveDeclarationViewTimer ()
		{
			if (declarationViewWindowOpacityTimer != 0) {
				GLib.Source.Remove (declarationViewWindowOpacityTimer);
				declarationViewWindowOpacityTimer = 0;
			}
			if (declarationViewTimer != 0) {
				GLib.Source.Remove (declarationViewTimer);
				declarationViewTimer = 0;
			}
		}

		void HideTooltip ()
		{
			RemoveDeclarationViewTimer ();
			if (declarationviewwindow != null) {
				declarationviewwindow.Hide ();
				declarationviewwindow.Opacity = 0;
			}
			if (tooltipSrc != null)
				tooltipSrc.Cancel ();
		}

		CancellationTokenSource tooltipSrc = null;
		async void ShowTooltip ()
		{
			HideTooltip ();
			var currentSelectedItem = selectedItem;
			if (currentSelectedItem == null || currentSelectedItem.DataSource == null)
				return;
			var i = currentSelectedItem.Item;
			if (i < 0 || i >= currentSelectedItem.DataSource.Count)
				return;

			if (tooltipSrc != null)
				tooltipSrc.Cancel ();
			tooltipSrc = new CancellationTokenSource ();
			var token = tooltipSrc.Token;

			TooltipInformation tooltip;
			try {
				tooltip = await currentSelectedItem.DataSource[i].GetTooltipInformation (token).ConfigureAwait (false);
			} catch (Exception e) {
				LoggingService.LogError ("Error while creating search popup window tooltip", e);
				return;
			}
			if (tooltip == null || string.IsNullOrEmpty (tooltip.SignatureMarkup) || token.IsCancellationRequested)
				return;
			
			declarationviewwindow.Clear ();
			declarationviewwindow.AddOverload (tooltip);
			declarationviewwindow.CurrentOverload = 0;
			declarationViewTimer = GLib.Timeout.Add (250, DelayedTooltipShow);
		}

		bool DelayedTooltipShow ()
		{
			declarationviewwindow.ShowArrow = true;
			var rect = SelectedItemRectangle;

			declarationviewwindow.ShowPopup (this, new Gdk.Rectangle (0, (int)rect.Y, Allocation.Width, (int)rect.Height), PopupPosition.Right);
			if (declarationViewWindowOpacityTimer != 0) 
				GLib.Source.Remove (declarationViewWindowOpacityTimer);
			declarationViewWindowOpacityTimer = GLib.Timeout.Add (50, new OpacityTimer (this).Timer);
			declarationViewTimer = 0;
			return false;
		}

		class OpacityTimer
		{
			public double Opacity { get; private set; }
			
			SearchPopupWindow window;
			//			static int num = 0;
			//			int id;
			public OpacityTimer (SearchPopupWindow window)
			{
				//				id = num++;
				this.window = window;
				Opacity = 0.0;
				window.declarationviewwindow.Opacity = Opacity;
			}
			
			public bool Timer ()
			{
				Opacity = System.Math.Min (1.0, Opacity + 0.33);
				window.declarationviewwindow.Opacity = Opacity;
				bool result = Math.Round (Opacity * 10.0) < 10;
				if (!result)
					window.declarationViewWindowOpacityTimer = 0;
				return result;
			}
		}



		void SelectNextCategory ()
		{
			if (selectedItem == null)
				return;
			var i = SelectedCategoryIndex;
			if (selectedItem.Equals (topItem)) {
				if (i > 0) {
					selectedItem = new ItemIdentifier (
						results [0].Item1,
						results [0].Item2,
						0
					);

				} else {
					if (topItem.DataSource.Count > 1) {
						selectedItem = new ItemIdentifier (
							results [0].Item1,
							results [0].Item2,
							1
						);
					} else if (i < results.Count - 1) {
						selectedItem = new ItemIdentifier (
							results [i + 1].Item1,
							results [i + 1].Item2,
							0
						);
					}
				}
			} else {
				while (i < results.Count - 1 && results [i + 1].Item2.Count == 0)
					i++;
				if (i < results.Count - 1) {
					selectedItem = new ItemIdentifier (
						results [i + 1].Item1,
						results [i + 1].Item2,
						0
					);
				}
			}
			QueueDraw ();	
		}

		void SelectPrevCategory ()
		{
			if (selectedItem == null)
				return;
			var i = SelectedCategoryIndex;
			if (i > 0) {
				selectedItem = new ItemIdentifier (
					results [i - 1].Item1,
					results [i - 1].Item2,
					0
				);
				if (selectedItem.Equals (topItem)) {
					if (topItem.DataSource.Count> 1) {
						selectedItem = new ItemIdentifier (
							results [i - 1].Item1,
							results [i - 1].Item2,
							1
						);
					} else if (i > 1) {
						selectedItem = new ItemIdentifier (
							results [i - 2].Item1,
							results [i - 2].Item2,
							0
						);
					}
				}
			} else {
				selectedItem = topItem;
			}
			QueueDraw ();
		}

		void SelectFirstCategory ()
		{
			selectedItem = topItem;
			QueueDraw ();
		}

		void SelectLastCatgory ()
		{
			var r = results.LastOrDefault (r2 => r2.Item2.Count > 0 && !(r2.Item2.Count == 1 && topItem.Category == r2.Item1));
			if (r == null)
				return;
			selectedItem = new ItemIdentifier (
				r.Item1,
				r.Item2,
				r.Item2.Count - 1
			);
			QueueDraw ();
		}

		internal bool ProcessKey (Xwt.Key key, Xwt.ModifierKeys state)
		{
			switch (key) {
			case Xwt.Key.Up:
				if (state.HasFlag (Xwt.ModifierKeys.Command))
					goto case Xwt.Key.PageUp;
				if (state.HasFlag (Xwt.ModifierKeys.Control))
					goto case Xwt.Key.Home;
				SelectItemUp ();
				return true;
			case Xwt.Key.Down:
				if (state.HasFlag (Xwt.ModifierKeys.Command))
					goto case Xwt.Key.PageDown;
				if (state.HasFlag (Xwt.ModifierKeys.Control))
					goto case Xwt.Key.End;
				SelectItemDown ();
				return true;
			case (Xwt.Key)Gdk.Key.KP_Page_Down:
			case Xwt.Key.PageDown:
				SelectNextCategory ();
				return true;
			case (Xwt.Key)Gdk.Key.KP_Page_Up:
			case Xwt.Key.PageUp:
				SelectPrevCategory ();
				return true;
			case Xwt.Key.Home:
				SelectFirstCategory ();
				return true;
			case Xwt.Key.End:
				SelectLastCatgory ();
				return true;
			case Xwt.Key.Return:
				OnItemActivated (EventArgs.Empty);
				return true;
			}
			return false;
		}

		public event EventHandler ItemActivated;

		protected virtual void OnItemActivated (EventArgs e)
		{
			EventHandler handler = this.ItemActivated;
			if (handler != null)
				handler (this, e);
		}

		public ISegment SelectedItemRegion {
			get {
				if (selectedItem == null || selectedItem.Item < 0 || selectedItem.Item >= selectedItem.DataSource.Count)
					return TextSegment.Invalid;
				return selectedItem.DataSource[selectedItem.Item].Segment;
			}
		}

		public string SelectedItemFileName {
			get {
				if (selectedItem == null || selectedItem.Item < 0 || selectedItem.Item >= selectedItem.DataSource.Count)
					return null;
				return selectedItem.DataSource[selectedItem.Item].File;
			}
		}

		class ItemIdentifier {
			public SearchCategory Category { get; private set; }
			public IReadOnlyList<SearchResult> DataSource { get; private set; }
			public int Item { get; private set; }

			public ItemIdentifier (SearchCategory category, IReadOnlyList<SearchResult> dataSource, int item)
			{
				this.Category = category;
				this.DataSource = dataSource;
				this.Item = item;
			}

			public override bool Equals (object obj)
			{
				if (obj == null)
					return false;
				if (ReferenceEquals (this, obj))
					return true;
				if (obj.GetType () != typeof(ItemIdentifier))
					return false;
				ItemIdentifier other = (ItemIdentifier)obj;
				return Category == other.Category && DataSource == other.DataSource && Item == other.Item;
			}
			
			public override int GetHashCode ()
			{
				unchecked {
					return (Category != null ? Category.GetHashCode () : 0) ^ (DataSource != null ? DataSource.GetHashCode () : 0) ^ Item.GetHashCode ();
				}
			}

			public override string ToString ()
			{
				return string.Format ("[ItemIdentifier: Category={0}, DataSource=#{1}, Item={2}]", Category.Name, DataSource.Count, Item);
			}
		}

		ItemIdentifier selectedItem = null, topItem = null;

		Cairo.Rectangle SelectedItemRectangle {
			get {
				if (selectedItem == null)
					return new Cairo.Rectangle (0, 0, Allocation.Width, 16);

				double y = ChildAllocation.Y + yMargin;
				if (topItem != null){
					layout.SetMarkup (GetRowMarkup (topItem.DataSource[topItem.Item]));
					int w, h;
					layout.GetPixelSize (out w, out h);
					if (topItem.Category == selectedItem.Category && topItem.Item == selectedItem.Item)
						return new Cairo.Rectangle (0, y, Allocation.Width, h + itemSeparatorHeight);
					y += h + itemSeparatorHeight;
				}
				foreach (var result in results) {
					var category = result.Item1;
					var dataSrc = result.Item2;
					int itemsAdded = 0;
					for (int i = 0; i < maxItems && i < dataSrc.Count; i++) {
						if (topItem != null && topItem.DataSource == dataSrc && topItem.Item == i)
							continue;
						layout.SetMarkup (GetRowMarkup (dataSrc[i]));

						int w, h;
						layout.GetPixelSize (out w, out h);

						if (selectedItem.Category == category && selectedItem.DataSource == dataSrc && selectedItem.Item == i)
							return new Cairo.Rectangle (0, y, Allocation.Width, h + itemSeparatorHeight);
						y += h + itemSeparatorHeight;

//						var region = dataSrc.GetRegion (i);
//						if (!region.IsEmpty) {
//							layout.SetMarkup (region.BeginLine.ToString ());
//							int w2, h2;
//							layout.GetPixelSize (out w2, out h2);
//							w += w2;
//						}
						itemsAdded++;
					}
					if (itemsAdded > 0)
						y += categorySeparatorHeight;
				}
				return new Cairo.Rectangle (0, 0, Allocation.Width, 16);
			}
		}

		protected override void OnDrawContent (Gdk.EventExpose evnt, Cairo.Context context)
		{
			context.LineWidth = 1;
			var alloc = ChildAllocation;
			var adjustedMarginSize = alloc.X - Allocation.X  + headerMarginSize;

			var r = results.Where (res => res.Item2.Count > 0).ToArray ();
			if (r.Any ()) {
				context.SetSourceColor (lightSearchBackground);
				context.Rectangle (Allocation.X, Allocation.Y, adjustedMarginSize, Allocation.Height);
				context.Fill ();

				context.SetSourceColor (darkSearchBackground);
				context.Rectangle (Allocation.X + adjustedMarginSize, Allocation.Y, Allocation.Width - adjustedMarginSize, Allocation.Height);
				context.Fill ();
				context.MoveTo (0.5 + Allocation.X + adjustedMarginSize, 0);
				context.LineTo (0.5 + Allocation.X + adjustedMarginSize, Allocation.Height);
				context.SetSourceColor (separatorLine);
				context.Stroke ();
			} else {
				context.SetSourceRGB (1, 1, 1);
				context.Rectangle (Allocation.X, Allocation.Y, Allocation.Width, Allocation.Height);
				context.Fill ();
			}

			double y = alloc.Y + yMargin;
			int w, h;
			if (topItem != null) {
				headerLayout.SetText (GettextCatalog.GetString ("Top Result"));
				headerLayout.GetPixelSize (out w, out h);
				context.MoveTo (alloc.Left + headerMarginSize - w - xMargin, y);
				context.SetSourceColor (headerColor);
				Pango.CairoHelper.ShowLayout (context, headerLayout);

				var category = topItem.Category;
				var dataSrc = topItem.DataSource;
				var i = topItem.Item;

				double x = alloc.X + xMargin + headerMarginSize;
				context.SetSourceRGB (0, 0, 0);
				layout.SetMarkup (GetRowMarkup (dataSrc[i]));
				layout.GetPixelSize (out w, out h);
				if (selectedItem != null && selectedItem.Category == category && selectedItem.Item == i) {
					context.SetSourceColor (selectionBackgroundColor);
					context.Rectangle (alloc.X + headerMarginSize, y, Allocation.Width - adjustedMarginSize, h);
					context.Fill ();
					context.SetSourceRGB (1, 1, 1);
				}

				var px = dataSrc[i].Icon;
				if (px != null) {
					context.DrawImage (this, px, (int)x + marginIconSpacing, (int)y + (h - px.Height) / 2);
					x += px.Width + iconTextSpacing + marginIconSpacing;
				}

				context.MoveTo (x, y);
				context.SetSourceRGB (0, 0, 0);
				Pango.CairoHelper.ShowLayout (context, layout);

				y += h + itemSeparatorHeight;

			}

			foreach (var result in r) {
				var category = result.Item1;
				var dataSrc = result.Item2;
				if (dataSrc.Count == 0)
					continue;
				if (dataSrc.Count == 1 && topItem != null && topItem.DataSource == dataSrc)
					continue;
				headerLayout.SetText (category.Name);
				headerLayout.GetPixelSize (out w, out h);

				if (y + h > Allocation.Height)
					break;

				context.MoveTo (alloc.X + headerMarginSize - w - xMargin, y);
				context.SetSourceColor (headerColor);
				Pango.CairoHelper.ShowLayout (context, headerLayout);

				layout.Width = Pango.Units.FromPixels (Allocation.Width - adjustedMarginSize - 35);

				for (int i = 0; i < maxItems && i < dataSrc.Count; i++) {
					if (topItem != null && topItem.Category == category && topItem.Item == i)
						continue;
					double x = alloc.X + xMargin + headerMarginSize;
					context.SetSourceRGB (0, 0, 0);
					layout.SetMarkup (GetRowMarkup (dataSrc[i]));
					layout.GetPixelSize (out w, out h);
					if (y + h + itemSeparatorHeight > Allocation.Height)
						break;
					if (selectedItem != null && selectedItem.Category == category && selectedItem.Item == i) {
						context.SetSourceColor (selectionBackgroundColor);
						context.Rectangle (alloc.X + headerMarginSize, y, Allocation.Width - adjustedMarginSize, h);
						context.Fill ();
						context.SetSourceRGB (1, 1, 1);
					}

					var px = dataSrc[i].Icon;
					if (px != null) {
						context.DrawImage (this, px, (int)x + marginIconSpacing, (int)y + (h - px.Height) / 2);
						x += px.Width + iconTextSpacing + marginIconSpacing;
					}

					context.MoveTo (x, y);
					context.SetSourceRGB (0, 0, 0);
					Pango.CairoHelper.ShowLayout (context, layout);

					y += h + itemSeparatorHeight;
				}
				if (result != r.Last ()) {
					y += categorySeparatorHeight;
				}
			}
			if (y == alloc.Y + yMargin) {
				context.SetSourceRGB (0, 0, 0);
				layout.SetMarkup (isInSearch ? GettextCatalog.GetString ("Searching...") : GettextCatalog.GetString ("No matches"));
				context.MoveTo (alloc.X + xMargin, y);
				Pango.CairoHelper.ShowLayout (context, layout);
			}
		}

		string GetRowMarkup (SearchResult result)
		{
			string txt = "<span foreground=\"#606060\">" + result.GetMarkupText(this) +"</span>";
			string desc = result.GetDescriptionMarkupText (this);
			if (!string.IsNullOrEmpty (desc))
				txt += "<span foreground=\"#8F8F8F\" size=\"small\">\n" + desc + "</span>";
			return txt;
		}
	}
}

