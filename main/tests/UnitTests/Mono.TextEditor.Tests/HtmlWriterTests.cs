// 
// HtmlWriterTests.cs
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
using Mono.TextEditor.Utils;
using NUnit.Framework;
using Mono.TextEditor.Highlighting;

namespace Mono.TextEditor.Tests
{
	[TestFixture()]
	public class HtmlWriterTests : TextEditorTestBase
	{
		[Test()]
		public void TestSimpleCSharpHtml ()
		{
			var data = Create ("class Foo {}");
			var style = SyntaxModeService.GetColorStyle ("TangoLight");
			ISyntaxMode mode = SyntaxModeService.GetSyntaxMode (data.Document, "text/x-csharp");
			string generatedHtml = HtmlWriter.GenerateHtml (data.Document, mode, style, data.Options);
			Assert.AreEqual (
				@"<!DOCTYPE HTML PUBLIC ""-//W3C//DTD HTML 4.0 Transitional//EN""><HTML><BODY><FONT face = 'Mono'><SPAN style = 'color:#009695;' >class</SPAN><SPAN style = 'color:#444444;' >&nbsp;Foo&nbsp;</SPAN><SPAN style = 'color:#444444;' >{}</SPAN></FONT></BODY></HTML>", generatedHtml);
		}

	}
}

