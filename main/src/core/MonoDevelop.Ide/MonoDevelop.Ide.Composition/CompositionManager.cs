﻿// CompositionManager.cs
//
// Author:
//   Kirill Osenkov <https://github.com/KirillOsenkov>
//
// Copyright (c) 2017 Microsoft
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
//
//

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Host;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.VisualStudio.Composition;
using Mono.Addins;
using MonoDevelop.Core;
using MonoDevelop.Core.AddIns;

namespace MonoDevelop.Ide.Composition
{
	/// <summary>
	/// The host of the MonoDevelop MEF composition. Uses https://github.com/Microsoft/vs-mef.
	/// </summary>
	public class CompositionManager
	{
		static Task<CompositionManager> creationTask;
		static CompositionManager instance;

		static readonly Resolver StandardResolver = Resolver.DefaultInstance;
		static readonly PartDiscovery Discovery = PartDiscovery.Combine (
			new AttributedPartDiscoveryV1 (StandardResolver),
			new AttributedPartDiscovery (StandardResolver, true));

		static List<ComposablePartDefinition> _partsExcludedFromImportTypes = new List<ComposablePartDefinition> ();
		static List<ComposablePartDefinition> _partsExcludedByTypes = new List<ComposablePartDefinition>();

		public static CompositionManager Instance {
			get {
				if (instance == null) {
					instance = InitializeAsync ().Result;
				}

				return instance;
			}
		}

		/// <summary>
		/// Starts initializing the MEF composition on a background thread. Thread-safe.
		/// </summary>
		public static Task<CompositionManager> InitializeAsync ()
		{
			if (creationTask == null) {
				lock (typeof (CompositionManager)) {
					if (creationTask == null) {
						creationTask = Task.Run (() => CreateInstanceAsync ());
					}
				}
			}

			return creationTask;
		}

		/// <summary>
		/// Returns an instance of type T that is exported by some composition part. The instance is shared (singleton).
		/// </summary>
		public static T GetExportedValue<T> ()
		{
			return Instance.ExportProvider.GetExportedValue<T> ();
		}

		public RuntimeComposition RuntimeComposition { get; private set; }
		public IExportProviderFactory ExportProviderFactory { get; private set; }
		public ExportProvider ExportProvider { get; private set; }
		public HostServices HostServices { get; private set; }
		public System.ComponentModel.Composition.Hosting.ExportProvider ExportProviderV1 { get; private set; }

		internal CompositionManager ()
		{
		}

		static async Task<CompositionManager> CreateInstanceAsync ()
		{
			var compositionManager = new CompositionManager ();
			await compositionManager.InitializeInstanceAsync ();
			return compositionManager;
		}

		async Task InitializeInstanceAsync ()
		{
			ComposableCatalog catalog = ComposableCatalog.Create (StandardResolver)
				.WithCompositionService ()
				.WithDesktopSupport ();

			var assemblies = new HashSet<Assembly> ();
			ReadAssembliesFromAddins (assemblies, "/MonoDevelop/Ide/TypeService/PlatformMefHostServices");
			ReadAssembliesFromAddins (assemblies, "/MonoDevelop/Ide/TypeService/MefHostServices");
			ReadAssembliesFromAddins (assemblies, "/MonoDevelop/Ide/Composition");

			// spawn discovery tasks in parallel for each assembly
			var tasks = new List<Task<DiscoveredParts>> (assemblies.Count);
			foreach (var assembly in assemblies) {
				var task = Task.Run (() => Discovery.CreatePartsAsync (assembly));
				tasks.Add (task);
			}

			foreach (var task in tasks) {
				var discoveredParts = await task;
				discoveredParts = new DiscoveredParts(discoveredParts.Parts.Where(p => ShouldInclude(p)), discoveredParts.DiscoveryErrors);
				catalog = catalog.AddParts(discoveredParts);
			}

			var discoveryErrors = catalog.DiscoveredParts.DiscoveryErrors;
			if (!discoveryErrors.IsEmpty) {
				var errorMessages = discoveryErrors.Select (x => x.Message).OrderBy (x => x);
				System.IO.File.WriteAllText (Path.Combine(Path.GetTempPath(), "composition_discovery_errors.txt"), String.Join (Environment.NewLine, errorMessages));

				//throw new ApplicationException ($"MEF catalog scanning errors encountered.\n{string.Join ("\n", discoveryErrors)}");
			}

			CompositionConfiguration configuration = CompositionConfiguration.Create (catalog);

			if (!configuration.CompositionErrors.IsEmpty) {
				// capture the errors in an array for easier debugging
				var errors = configuration.CompositionErrors.ToArray ();

				// For now while we're still transitioning to VSMEF it's useful to work
				// even if the composition has some errors. TODO: re-enable this.
				//var messages = errors.SelectMany (e => e).Select (e => e.Message);
				//var text = string.Join (Environment.NewLine, messages);
				//Xwt.Clipboard.SetText (text);
				var errorMessages = configuration.CompositionErrors.SelectMany(x => x).Select(x => x.Message).OrderBy(x => x);
				System.IO.File.WriteAllText (Path.Combine (Path.GetTempPath (), "composition_errors.txt"), String.Join (Environment.NewLine, errorMessages));
				//configuration.ThrowOnErrors ();
			}

			if (_partsExcludedFromImportTypes.Count > 0) {
				var excludedParts = _partsExcludedFromImportTypes.Select (x => x.Id).OrderBy (x => x);
				System.IO.File.WriteAllText (Path.Combine (Path.GetTempPath (), "composition_partsExcludedFromImportTypes.txt"), String.Join (Environment.NewLine, excludedParts));
			}

			if (_partsExcludedByTypes.Count > 0) {
				var excludedParts = _partsExcludedByTypes.Select (x => x.Id).OrderBy (x => x);
				System.IO.File.WriteAllText (Path.Combine (Path.GetTempPath (), "composition_partsExcludedByTypes.txt"), String.Join (Environment.NewLine, excludedParts));
			}

			var typeRefs = configuration.Catalog.Parts.Select (x => x.Id).OrderBy(x => x);
			System.IO.File.WriteAllText (@"c:\composition_success.txt", String.Join (Environment.NewLine, typeRefs));

			RuntimeComposition = RuntimeComposition.CreateRuntimeComposition (configuration);
			ExportProviderFactory = RuntimeComposition.CreateExportProviderFactory ();
			ExportProvider = ExportProviderFactory.CreateExportProvider ();
			HostServices = MefV1HostServices.Create (ExportProvider.AsExportProvider ());
			ExportProviderV1 = NetFxAdapters.AsExportProvider (ExportProvider);
		}

		bool ShouldInclude (ComposablePartDefinition part)
		{
			if (part.Imports.Any(x => _excludedImportTypes.Contains(x.ImportingSiteType.FullName)))
			{
				_partsExcludedFromImportTypes.Add (part);
				return false;
			}
			else if (_excludedTypes.Contains(part.Type.FullName))
			{
				_partsExcludedByTypes.Add(part);
				return false;
			}

			return true;
		}

		static HashSet<string> _excludedImportTypes = new HashSet<string> (StringComparer.Ordinal) {
			"Microsoft.VisualStudio.Language.Intellisense.ICompletionBroker",
			"Microsoft.VisualStudio.Language.Intellisense.IGlyphService",
			"Microsoft.VisualStudio.Language.Intellisense.IPeekResultFactory",
			"Microsoft.VisualStudio.Language.Intellisense.IQuickInfoBroker",
			"Microsoft.VisualStudio.Language.Intellisense.ISignatureHelpBroker",
		};

		static HashSet<string> _excludedTypes = new HashSet<string>(StringComparer.Ordinal) {
			// Excluded because RenameCommandHandler.ExecuteCommand references IWpfTextView.VisualElement during processing return
			"Microsoft.CodeAnalysis.Editor.Implementation.InlineRename.RenameCommandHandler",
		};

		void ReadAssembliesFromAddins (HashSet<Assembly> assemblies, string extensionPath)
		{
			foreach (var node in AddinManager.GetExtensionNodes (extensionPath)) {
				var assemblyNode = node as AssemblyExtensionNode;
				if (assemblyNode != null) {
					try {
						// Make sure the add-in that registered the assembly is loaded, since it can bring other
						// other assemblies required to load this one
						AddinManager.LoadAddin (null, assemblyNode.Addin.Id);

						var assemblyFilePath = assemblyNode.Addin.GetFilePath (assemblyNode.FileName);
						var assembly = Runtime.SystemAssemblyService.LoadAssemblyFrom (assemblyFilePath);
						assemblies.Add (assembly);
					}
					catch (Exception e) {
						LoggingService.LogError ("Composition can't load assembly " + assemblyNode.FileName, e);
					}
				}
			}
		}
	}
}