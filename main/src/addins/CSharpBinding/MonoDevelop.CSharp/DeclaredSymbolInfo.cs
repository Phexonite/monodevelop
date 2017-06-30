//
// DeclaredSymbolInfo.cs
//
// Author:
//       Mike Krüger <mkrueger@xamarin.com>
//
// Copyright (c) 2015 Xamarin Inc. (http://xamarin.com)
// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.
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

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.NavigateTo;
using MonoDevelop.Ide.CodeCompletion;
using MonoDevelop.Ide.TypeSystem;
using MonoDevelop.Core;
using Gtk;
using MonoDevelop.Ide;
using MonoDevelop.Components.MainToolbar;
using MonoDevelop.Ide.Gui;
using System.Text;
using MonoDevelop.Ide.Editor.Highlighting;
using Microsoft.CodeAnalysis;
using System.Collections.Generic;
using MonoDevelop.Ide.Editor;

namespace MonoDevelop.CSharp
{
	class DeclaredSymbolInfoResult : SearchResult
	{
		bool useFullName;
		INavigateToSearchResult result;

		public override SearchResultType SearchResultType { get { return SearchResultType.Type; } }

		public override string File {
			get { return result.NavigableItem.Document.FilePath; }
		}

		public override Xwt.Drawing.Image Icon {
			get {
				return ImageService.GetIcon (result.GetStockIconForNavigableItem(), IconSize.Menu);
			}
		}

		public override int Offset {
			get { return result.NavigableItem.SourceSpan.Start; }
		}

		public override int Length {
			get { return result.NavigableItem.SourceSpan.Length; }
		}

		public override string PlainText {
			get {
				return result.Name;
			}
		}

		public override Task<TooltipInformation> GetTooltipInformation (CancellationToken token)
		{
			var theme = DefaultSourceEditorOptions.Instance.GetEditorTheme ();
			var sb = new StringBuilder ();
			TaggedTextUtil.AppendTaggedText (sb, theme, result.NavigableItem.DisplayTaggedParts);

			return Task.FromResult (new TooltipInformation {
				SignatureMarkup = sb.ToString (),
				SummaryMarkup = result.Summary,
				FooterMarkup = result.AdditionalInformation,
			});
		}

		public override string Description {
			get {
				string loc = GettextCatalog.GetString ("file {0}", File);
				return result.GetDisplayStringForNavigableItem (loc);
			}
		}

		public override string GetMarkupText (bool selected)
		{
			// use tagged markers
			return HighlightMatch (result.Name, match, selected);
		}

		public DeclaredSymbolInfoResult (string match, string matchedString, int rank, INavigateToSearchResult result)  : base (match, matchedString, rank)
		{
			this.result = result;
		}

		public override bool CanActivate {
			get {
				return result.NavigableItem.Document != null;
			}
		}

		public override async void Activate ()
		{
			var filePath = result.NavigableItem.Document.FilePath;
			var offset = result.NavigableItem.SourceSpan.Start;

			var proj = TypeSystemService.GetMonoProject (result.NavigableItem.Document.Project);
			if (proj?.ParentSolution != null) {
				string projectedName;
				int projectedOffset;
				if (TypeSystemService.GetWorkspace (proj.ParentSolution).TryGetOriginalFileFromProjection (filePath, offset, out projectedName, out projectedOffset)) {
					filePath = projectedName;
					offset = projectedOffset;
				}
			}

			await IdeApp.Workbench.OpenDocument (new FileOpenInformation (filePath, proj) {
				Offset = offset
			});
		}

		static class TaggedTextUtil
		{
			public static void AppendTaggedText (StringBuilder markup, EditorTheme theme, IEnumerable<TaggedText> text)
			{
				foreach (var part in text) {
					if (part.Tag != TextTags.Text) {
						markup.Append ("<span foreground=\"");
						markup.Append (GetThemeColor (theme, GetThemeColor (part.Tag)));
						markup.Append ("\">");
					}
					markup.Append (GLib.Markup.EscapeText (part.Text));
					if (part.Tag != TextTags.Text) {
						markup.Append ("</span>");
					}
				}
			}

			static string GetThemeColor (EditorTheme theme, string scope)
			{
				return SyntaxHighlightingService.GetColorFromScope (theme, scope, EditorThemeColors.Foreground).ToPangoString ();
			}

			static string GetThemeColor (string tag)
			{
				switch (tag) {
				case TextTags.Keyword:
					return "keyword";

				case TextTags.Class:
					return EditorThemeColors.UserTypes;
				case TextTags.Delegate:
					return EditorThemeColors.UserTypesDelegates;
				case TextTags.Enum:
					return EditorThemeColors.UserTypesEnums;
				case TextTags.Interface:
					return EditorThemeColors.UserTypesInterfaces;
				case TextTags.Module:
					return EditorThemeColors.UserTypes;
				case TextTags.Struct:
					return EditorThemeColors.UserTypesValueTypes;
				case TextTags.TypeParameter:
					return EditorThemeColors.UserTypesTypeParameters;

				case TextTags.Alias:
				case TextTags.Assembly:
				case TextTags.Field:
				case TextTags.ErrorType:
				case TextTags.Event:
				case TextTags.Label:
				case TextTags.Local:
				case TextTags.Method:
				case TextTags.Namespace:
				case TextTags.Parameter:
				case TextTags.Property:
				case TextTags.RangeVariable:
					return "source.cs";

				case TextTags.NumericLiteral:
					return "constant.numeric";

				case TextTags.StringLiteral:
					return "string.quoted";

				case TextTags.Space:
				case TextTags.LineBreak:
					return "source.cs";

				case TextTags.Operator:
					return "keyword.source";

				case TextTags.Punctuation:
					return "punctuation";

				case TextTags.AnonymousTypeIndicator:
				case TextTags.Text:
					return "source.cs";

				default:
					LoggingService.LogWarning ("Warning unexpected text tag: " + tag);
					return "source.cs";
				}
			}
		}
	}
}
