using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Jellyfin.Plugin.JellyP.Configuration;
using Jellyfin.Plugin.JellyP.Infrastructure;
using Jellyfin.Plugin.JellyP.Migration;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Querying;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;

internal static class Program
{
    private const int MetadataCount = 100_000;
    private const int BackupCount = 10_000;
    private const int ApplyCount = 100;
    private const int Runs = 3;
    private static readonly string BaseRoot = Path.Combine(Path.GetTempPath(), "jellyp-benchmark-" + Environment.ProcessId);

    public static async Task<int> Main(string[] args)
    {
        var output = args.Length > 0 ? args[0] : Path.Combine(BaseRoot, "result.json");
        Directory.CreateDirectory(BaseRoot);
        var mode = Mode;
        var pluginAssembly = typeof(JellyfinArtworkStore).Assembly;
        var pluginHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(pluginAssembly.Location)));
        var results = new List<CaseResult>();
        var metadataRoot = Path.Combine(BaseRoot, "metadata-preview");
        var previewItems = CreateItems(metadataRoot, createHistoryOutputs: false);
        var previewLibrary = new FakeLibrary(previewItems);

        results.Add(MeasureCase("preview-repeat-page1-x5", MeasurePreview(previewLibrary, metadataRoot),
            "5 repeated first-page requests for the same 1,000 rows; fake library, missing image paths, no file I/O"));
        results.Add(MeasureCase("preview-common-5000-rows", MeasureCommon5000(previewLibrary, metadataRoot),
            "one DetailedInventory(maximumEntries=5000) on both APIs; returns 5,001 rows; fake library, missing image paths"));
        results.Add(MeasureCase("inventory-global-100k", MeasureInventory(previewLibrary, metadataRoot),
            "one Inventory() over 100,000 fake metadata items; missing image paths, no image decoding"));
#if CANDIDATE
        results.Add(MeasureCase("preview-continuation-pages-2-5", MeasureContinuation(previewLibrary, metadataRoot),
            "candidate-only continuation: first page + four cursor pages, 5,001 rows total; baseline API cannot continue"));
#endif
        results.Add(MeasureCase("plan-100k-bounded-part", MeasurePlan100k(previewItems, metadataRoot),
            "AnalyzePlan over 100,000 fake metadata items; baseline is expected to fail PlanScopeTooLarge, candidate to return a bounded part"));

        var historyRoot = Path.Combine(BaseRoot, "history");
        var historyMetadata = Path.Combine(BaseRoot, "metadata-history");
        var historyItems = CreateItems(historyMetadata, createHistoryOutputs: true);
        var historyLibrary = new FakeLibrary(historyItems);
        var (journalFiles, journalBytes) = CreateHistory(historyRoot, historyItems);
        var historyStore = new CountingStore(CreateStore(historyLibrary, historyMetadata));
        var historyConfig = new PluginConfiguration
        {
            Enabled = true,
            DryRun = false,
            MinimumFreeSpaceBytes = 64L * 1024 * 1024,
            MaximumBackupBytes = 512L * 1024 * 1024,
            BackupRetentionDays = 0,
        };
        var historyCoordinator = new OptimizationCoordinator(historyStore, () => historyConfig, historyRoot,
            NullLogger<OptimizationCoordinator>.Instance);
        results.Add(MeasureCase("history-getbackups-all-page50", MeasureHistory(historyLibrary, historyStore, historyCoordinator,
                c => c.GetBackups("all", 0, 50, default), journalFiles, journalBytes, "GetBackups all page of 50 from 10,000 committed backups"),
            "10,000 journal JSON files and 10,000 3-byte backup originals; candidate default 'all' is preliminary metadata only, baseline 0.4.0.0 'all' performed verification. Do not compare this row as equivalent work."));
        results.Add(MeasureCase("history-getbackupkeys-all", MeasureHistory(historyLibrary, historyStore, historyCoordinator,
                c => c.GetBackupKeys("all", default), journalFiles, journalBytes, "GetBackupKeys all over 10,000 committed backups"),
            "10,000 journal JSON files and 3-byte originals; candidate 'all' is preliminary, baseline 'all' was verified. Separate semantic rows; no huge originals"));
        results.Add(MeasureCase("history-getbackups-restorable-explicit-page50", MeasureHistory(historyLibrary, historyStore, historyCoordinator,
                c => c.GetBackups("restorable", 0, 50, default), journalFiles, journalBytes, "GetBackups explicitly verified restorable page of 50"),
            "Same explicit verified semantics on both builds; 10,000 tiny originals are hashed on both sides. This is the comparable verified-request row."));
        results.Add(MeasureCase("history-getbackupkeys-restorable-explicit", MeasureHistory(historyLibrary, historyStore, historyCoordinator,
                c => c.GetBackupKeys("restorable", default), journalFiles, journalBytes, "GetBackupKeys explicitly verified restorable"),
            "Same explicit verified semantics on both builds; 10,000 tiny originals are hashed on both sides. This is the comparable verified-request row."));

        var applyRuns = new List<Sample>();
        for (var run = 0; run < Runs; run++)
            applyRuns.Add(await MeasureApply(Path.Combine(BaseRoot, "apply", "run" + run)));
        results.Add(MeasureCase("apply-100-no-keep", applyRuns,
            "AnalyzePlan + ApplyPlan + RunNativeAsync for 100 synthetic PNGs with PreserveOriginals=false; fake store scans 100,000 reference paths per CountPathReferences call"));

        var document = new
        {
            schema = 1,
            mode,
            createdUtc = DateTime.UtcNow,
            baseRoot = BaseRoot,
            plugin = new
            {
                version = pluginAssembly.GetName().Version?.ToString(),
                location = pluginAssembly.Location,
                sha256 = pluginHash,
            },
            environment = new
            {
                runtime = Environment.Version.ToString(),
                os = Environment.OSVersion.ToString(),
                processorCount = Environment.ProcessorCount,
                stopwatchFrequency = Stopwatch.Frequency,
                metadataCount = MetadataCount,
                backupCount = BackupCount,
                applyCount = ApplyCount,
                runsPerCase = Runs,
            },
            cases = results,
            limitations = new[]
            {
                "Fake Jellyfin library and fake artwork store; not a real Jellyfin server and not production latency.",
                "100,000 metadata paths are synthetic/virtual; image bytes are tiny or absent and are not decoded for the metadata scan.",
                "10,000 backup records use tiny synthetic originals; GetCurrent is an O(1) dictionary and originals are not hashed.",
                "Apply uses a fake IArtworkStore with 100 real tiny PNGs and a synthetic 100,000-path reference scan.",
                "Stopwatch values are harness medians for the same process/input, not throughput or production capacity.",
                "The baseline 0.4.0.0 plan path cannot continue past its 5,000-row cap; the 100k plan case records success/failure instead of a time ratio.",
            },
        };
        var json = JsonSerializer.Serialize(document, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(output, json + "\n");
        Console.WriteLine(json);
        return 0;
    }

    private static string Mode =>
#if CANDIDATE
        "candidate-0.4.0.1";
#else
        "baseline-0.4.0.0";
#endif

    private static CaseResult MeasureCase(string name, List<Sample> runs, string note)
    {
        var counts = runs[0];
        var median = Median(runs.Select(run => run.Ms));
        return new CaseResult(
            name,
            runs.Count,
            median,
            Median(runs.Where(run => run.AnalysisMs.HasValue).Select(run => run.AnalysisMs!.Value)),
            Median(runs.Where(run => run.ApplyMs.HasValue).Select(run => run.ApplyMs!.Value)),
            counts,
            note);
    }

    private static double Median(IEnumerable<double> values)
    {
        var ordered = values.OrderBy(value => value).ToArray();
        return ordered.Length == 0 ? 0 : ordered[ordered.Length / 2];
    }

    private static List<Sample> MeasurePreview(FakeLibrary library, string metadataRoot)
    {
        var runs = new List<Sample>();
        for (var run = 0; run < Runs; run++)
        {
            library.ResetCounters();
            var store = CreateStore(library, metadataRoot);
            var watch = Stopwatch.StartNew();
#if CANDIDATE
            var rows = 0;
            for (var page = 0; page < 5; page++)
                rows += store.DetailedInventoryPage(default, maximumEntries: 1000).Count;
#else
            var rows = 0;
            for (var page = 0; page < 5; page++)
                rows += store.DetailedInventory(default).Take(1001).Count();
#endif
            watch.Stop();
            runs.Add(NewSample(watch, library, rows,
#if CANDIDATE
                "5 repeated page-1 requests; 5,005 rows including overflow markers"
#else
                "baseline has no bounded page API: 5 refreshes each materialized all 100,000 entries; 5,005 rows were usable"
#endif
            ));
        }
        return runs;
    }

    private static List<Sample> MeasureCommon5000(FakeLibrary library, string metadataRoot)
    {
        var runs = new List<Sample>();
        for (var run = 0; run < Runs; run++)
        {
            library.ResetCounters();
            var store = CreateStore(library, metadataRoot);
            var watch = Stopwatch.StartNew();
            var rows = 0;
            var note = "success";
            try
            {
                rows = store.DetailedInventory(default, maximumEntries: 5000).Count;
            }
            catch (JellyPRequestException exception)
            {
                note = $"failed code={exception.Code}";
            }
            watch.Stop();
            runs.Add(NewSample(watch, library, rows, note));
        }
        return runs;
    }

    private static List<Sample> MeasureInventory(FakeLibrary library, string metadataRoot)
    {
        var runs = new List<Sample>();
        for (var run = 0; run < Runs; run++)
        {
            library.ResetCounters();
            var store = CreateStore(library, metadataRoot);
            var watch = Stopwatch.StartNew();
            var rows = store.Inventory(default);
            watch.Stop();
            runs.Add(NewSample(watch, library, rows: rows.Count, note: "one-global-inventory"));
        }
        return runs;
    }

#if CANDIDATE
    private static List<Sample> MeasureContinuation(FakeLibrary library, string metadataRoot)
    {
        var runs = new List<Sample>();
        for (var run = 0; run < Runs; run++)
        {
            library.ResetCounters();
            var store = CreateStore(library, metadataRoot);
            var watch = Stopwatch.StartNew();
            string? cursor = null;
            var rows = 0;
            for (var page = 0; page < 5; page++)
            {
                var current = store.DetailedInventoryPage(default, afterKey: cursor, maximumEntries: 1000);
                rows += current.Count;
                if (current.Count == 0) throw new InvalidOperationException("Candidate continuation returned an empty page.");
                cursor = current[^1].Reference.Key;
            }
            watch.Stop();
            runs.Add(NewSample(watch, library, rows, "first page + 4 cursor pages; 5,005 rows including overflow markers"));
        }
        return runs;
    }
#endif

    private static List<Sample> MeasurePlan100k(List<BaseItem> items, string metadataRoot)
    {
        var runs = new List<Sample>();
        for (var run = 0; run < Runs; run++)
        {
            var library = new FakeLibrary(items);
            var store = CreateStore(library, metadataRoot);
            var root = Path.Combine(BaseRoot, "plan", "run" + run);
            Directory.CreateDirectory(root);
            var config = new PluginConfiguration
            {
                Enabled = true,
                DryRun = false,
                PreserveOriginals = true,
                MinimumSavingsBytes = 0,
                MinimumSavingsRatio = 0,
                MinimumSsim = 0,
                MinimumFreeSpaceBytes = 64L * 1024 * 1024,
                MaximumBackupBytes = 512L * 1024 * 1024,
                BackupRetentionDays = 0,
                BatchSize = 1000,
            };
            var coordinator = new OptimizationCoordinator(store, () => config, root,
                NullLogger<OptimizationCoordinator>.Instance);
            var watch = Stopwatch.StartNew();
            var note = "success";
            var rows = 0;
            try
            {
                var plan = coordinator.AnalyzePlan(new PlanAnalyzeRequest
                {
                    Scope = "all",
                    BatchSize = 1000,
                    PreserveOriginals = true,
                    GeneralPolicy = new PlanGeneralPolicy { Quality = 60, MaximumDimension = 2400, MinimumSsim = 0 },
                }, default);
                rows = plan.TotalImages;
#if CANDIDATE
                note = $"success total={plan.TotalImages} hasMore={plan.HasMore}";
#else
                note = $"success total={plan.TotalImages}";
#endif
            }
            catch (JellyPRequestException exception)
            {
                note = $"failed code={exception.Code}";
            }
            watch.Stop();
            runs.Add(NewSample(watch, library, rows, note));
        }
        return runs;
    }

    private static List<Sample> MeasureHistory(FakeLibrary library, CountingStore store, OptimizationCoordinator coordinator,
        Func<OptimizationCoordinator, object> call, int journalFiles, long journalBytes, string note)
    {
        var runs = new List<Sample>();
        for (var run = 0; run < Runs; run++)
        {
            library.ResetCounters();
            store.ResetCounters();
            var watch = Stopwatch.StartNew();
            var result = call(coordinator);
            watch.Stop();
            var rows = result switch
            {
                BackupPage page => page.Items.Count,
                BackupKeysResponse keys => keys.Keys.Count,
                _ => 0,
            };
            var sample = NewSample(watch, library, rows, note) with
            {
                GetCurrent = store.GetCurrentCalls,
                CountRefSingle = store.CountRefCalls,
                JsonFiles = journalFiles,
                JsonBytes = journalBytes,
                Note = note + $"; inventoryCalls={store.InventoryCalls}; detailedInventoryCalls={store.DetailedInventoryCalls}; detailedPageCalls={store.DetailedPageCalls}; slotSafetyCalls={store.SlotSafetyCalls}",
            };
            runs.Add(sample);
        }
        return runs;
    }

    private static async Task<Sample> MeasureApply(string root)
    {
        Directory.CreateDirectory(root);
        var store = new ApplyStore(root);
        store.AddApplyItems();
        store.AddReferencePaths(100_000);
        var config = new PluginConfiguration
        {
            Enabled = true,
            DryRun = false,
            PreserveOriginals = false,
            MinimumSavingsBytes = 0,
            MinimumSavingsRatio = 0,
            MinimumSsim = 0,
            MinimumFreeSpaceBytes = 64L * 1024 * 1024,
            MaximumBackupBytes = 512L * 1024 * 1024,
            BackupRetentionDays = 0,
            BatchSize = ApplyCount,
        };
        var coordinator = new OptimizationCoordinator(store, () => config, Path.Combine(root, "migration"),
            NullLogger<OptimizationCoordinator>.Instance);
        store.ResetCounters();
        var watch = Stopwatch.StartNew();
        var plan = coordinator.AnalyzePlan(new PlanAnalyzeRequest
        {
            Scope = "all",
            BatchSize = ApplyCount,
            PreserveOriginals = false,
            GeneralPolicy = new PlanGeneralPolicy { Quality = 60, MaximumDimension = 2400, MinimumSsim = 0 },
        }, default);
        var analysisMs = watch.Elapsed.TotalMilliseconds;
        var analysisInventoryCalls = store.InventoryCalls;
        var keys = coordinator.GetPlanKeys(plan.PlanId, "selectable").Keys.ToArray();
        if (keys.Length != ApplyCount)
            throw new InvalidOperationException($"Expected {ApplyCount} apply keys, got {keys.Length}.");
        store.ResetCounters();
        watch.Restart();
        var receipt = coordinator.ApplyPlan(plan.PlanId, new PlanApplyRequest
        {
            Keys = keys,
            AuthorizePermanentRemoval = true,
        });
        await coordinator.RunNativeAsync(new Progress<double>(), default).ConfigureAwait(false);
        watch.Stop();
        var operation = coordinator.GetOperation(receipt.OperationId, 0, 10).Operation;
        if (operation.State != "Completed" || operation.Changed != ApplyCount || store.SaveCalls != ApplyCount)
            throw new InvalidOperationException($"Apply did not complete cleanly: state={operation.State} changed={operation.Changed} saves={store.SaveCalls}.");
        return new Sample(
            analysisMs + watch.Elapsed.TotalMilliseconds,
            analysisMs,
            watch.Elapsed.TotalMilliseconds,
            store.GetItemIdsCalls,
            store.GetItemListCalls,
            store.ItemsHydrated,
            store.GetCurrentCalls,
            store.CountRefSingle,
            store.CountRefCollection,
            store.RefScanRows,
            store.SaveCalls,
            0,
            0,
            keys.Length,
            $"analysisInventoryCalls={analysisInventoryCalls} operationState={operation.State}");
    }

    private static Sample NewSample(Stopwatch watch, FakeLibrary library, int rows, string note) => new(
        watch.Elapsed.TotalMilliseconds,
        null,
        null,
        library.GetItemIdsCalls,
        library.GetItemListCalls,
        library.ItemsHydrated,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        rows,
        note);

    private static List<BaseItem> CreateItems(string metadataRoot, bool createHistoryOutputs)
    {
        var items = new List<BaseItem>(MetadataCount);
        for (var index = 0; index < MetadataCount; index++)
        {
            var id = new Guid(index + 1, 0, 0, new byte[8]);
            string path;
            if (createHistoryOutputs && index < BackupCount)
            {
                path = Path.Combine(metadataRoot, "history", id.ToString("N") + ".webp");
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(path, [1, 2, 3]);
            }
            else
            {
                path = Path.Combine(metadataRoot, "large", id.ToString("N"), "poster.png");
            }

            items.Add(new Person
            {
                Id = id,
                Name = "Synthetic metadata " + index,
                Path = Path.GetDirectoryName(path),
                ImageInfos = [new ItemImageInfo { Type = ImageType.Primary, Path = path }],
            });
        }

        return items;
    }

    private static (int Files, long Bytes) CreateHistory(string root, List<BaseItem> items)
    {
        var journal = Path.Combine(root, "journal");
        var originals = Path.Combine(root, "originals");
        Directory.CreateDirectory(journal);
        Directory.CreateDirectory(originals);
        var seed = Path.Combine(root, "seed.original");
        File.WriteAllBytes(seed, [1, 2, 3]);
        var contentHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(new byte[] { 1, 2, 3 }));
        long bytes = 0;
        for (var index = 0; index < BackupCount; index++)
        {
            var item = items[index];
            var id = item.Id;
            var source = Path.Combine(root, "source", id.ToString("N") + ".png");
            var output = item.ImageInfos[0].Path;
            var record = new MigrationRecord
            {
                Id = id,
                Source = new ArtworkReference(id, ImageType.Primary, 0, source),
                SourceHash = contentHash,
                OutputHash = contentHash,
                OriginalMimeType = "image/png",
                OriginalBytes = 3,
                OutputBytes = 3,
                OutputPath = output,
                CreatedUtc = DateTime.UtcNow.AddSeconds(-index - 1),
                CompletedUtc = DateTime.UtcNow.AddSeconds(-index),
                State = "Committed",
                BackupDeleted = false,
            };
            var json = JsonSerializer.SerializeToUtf8Bytes(record);
            File.WriteAllBytes(Path.Combine(journal, id.ToString("N") + ".json"), json);
            bytes += json.Length;
            File.Copy(seed, Path.Combine(originals, id.ToString("N") + ".original"), overwrite: true);
        }

        return (BackupCount, bytes);
    }

    private static JellyfinArtworkStore CreateStore(FakeLibrary library, string metadataRoot)
    {
        var paths = Proxy<IServerApplicationPaths>((method, _) => method.Name == "get_InternalMetadataPath"
            ? metadataRoot
            : throw new NotSupportedException(method.Name));
        var configuration = Proxy<IServerConfigurationManager>((method, _) => method.Name == "get_ApplicationPaths"
            ? paths
            : throw new NotSupportedException(method.Name));
        var providers = Proxy<IProviderManager>((method, _) => method.Name switch
        {
            "GetRefreshQueue" => new HashSet<Guid>(),
            "GetRefreshProgress" => null,
            _ => throw new NotSupportedException(method.Name),
        });
        var manager = Proxy<ILibraryManager>((method, args) => library.Handle(method, args));
        return new JellyfinArtworkStore(manager, providers, configuration);
    }

    private static T Proxy<T>(Func<MethodInfo, object?[]?, object?> handler) where T : class
    {
        var proxy = DispatchProxy.Create<T, CallbackProxy>();
        ((CallbackProxy)(object)proxy).Handler = handler;
        return proxy;
    }

    public class CallbackProxy : DispatchProxy
    {
        public Func<MethodInfo, object?[]?, object?>? Handler { get; set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Handler!(targetMethod!, args);
    }

    private sealed class FakeLibrary
    {
        private List<BaseItem> _items = [];
        private Dictionary<Guid, BaseItem> _byId = [];
        public long GetItemIdsCalls { get; private set; }
        public long GetItemListCalls { get; private set; }
        public long ItemsHydrated { get; private set; }
        public long GetItemByIdCalls { get; private set; }
        public bool ScanRunning { get; set; }

        public FakeLibrary()
        {
        }

        public FakeLibrary(List<BaseItem> items) => Load(items);

        public void Load(List<BaseItem> items)
        {
            _items = items;
            _byId = items.ToDictionary(item => item.Id);
        }

        public void ResetCounters()
        {
            GetItemIdsCalls = 0;
            GetItemListCalls = 0;
            ItemsHydrated = 0;
            GetItemByIdCalls = 0;
        }

        public object? Handle(MethodInfo method, object?[]? args) => method.Name switch
        {
            "get_IsScanRunning" => ScanRunning,
            "GetItemIds" => GetItemIds(),
            "GetItemList" => GetItemList((InternalItemsQuery)args![0]!),
            "GetItemById" => GetItemById((Guid)args![0]!),
            "GetVirtualFolders" => new List<VirtualFolderInfo>(),
            "GetCollectionFolders" => new List<Folder>(),
            "GetPeopleByItems" => new Dictionary<Guid, IReadOnlyList<PersonInfo>>(),
            "GetPersonId" => throw new NotSupportedException("GetPersonId is not used by this benchmark."),
            "UpdateImagesAsync" => Task.CompletedTask,
            _ => throw new NotSupportedException(method.Name),
        };

        private Guid[] GetItemIds()
        {
            GetItemIdsCalls++;
            return _items.Select(item => item.Id).ToArray();
        }

        private BaseItem[] GetItemList(InternalItemsQuery query)
        {
            GetItemListCalls++;
            var ids = query.ItemIds.ToHashSet();
            var selected = _items.Where(item => ids.Contains(item.Id)).ToArray();
            ItemsHydrated += selected.Length;
            return selected;
        }

        private BaseItem? GetItemById(Guid id)
        {
            GetItemByIdCalls++;
            return _byId.GetValueOrDefault(id);
        }
    }

    private sealed class CountingStore(IArtworkStore inner) : IArtworkStore
    {
        public long InventoryCalls { get; private set; }
        public long DetailedInventoryCalls { get; private set; }
        public long DetailedPageCalls { get; private set; }
        public long GetCurrentCalls { get; private set; }
        public long CountRefCalls { get; private set; }
        public long SlotSafetyCalls { get; private set; }

        public void ResetCounters()
        {
            InventoryCalls = 0;
            DetailedInventoryCalls = 0;
            DetailedPageCalls = 0;
            GetCurrentCalls = 0;
            CountRefCalls = 0;
            SlotSafetyCalls = 0;
        }

        public IReadOnlyList<ArtworkReference> Inventory(CancellationToken cancellationToken,
            IReadOnlyCollection<Guid>? includedItemIds = null, IReadOnlyCollection<Guid>? includedLibraryIds = null,
            Action<int>? inventoryProgress = null)
        {
            InventoryCalls++;
            return inner.Inventory(cancellationToken, includedItemIds, includedLibraryIds, inventoryProgress);
        }

        public IReadOnlyList<ArtworkInventoryEntry> DetailedInventory(CancellationToken cancellationToken,
            IReadOnlyCollection<Guid>? includedItemIds = null, IReadOnlyCollection<Guid>? includedLibraryIds = null,
            Action<int>? inventoryProgress = null, int? maximumEntries = null)
        {
            DetailedInventoryCalls++;
            return inner.DetailedInventory(cancellationToken, includedItemIds, includedLibraryIds, inventoryProgress, maximumEntries);
        }

        public IReadOnlyList<ArtworkInventoryEntry> DetailedInventoryPage(CancellationToken cancellationToken,
            IReadOnlyCollection<Guid>? includedItemIds = null, IReadOnlyCollection<Guid>? includedLibraryIds = null,
            string? afterKey = null, int? maximumEntries = null, Action<int>? inventoryProgress = null)
        {
            DetailedPageCalls++;
#if CANDIDATE
            return inner.DetailedInventoryPage(cancellationToken, includedItemIds, includedLibraryIds, afterKey, maximumEntries, inventoryProgress);
#else
            return inner.DetailedInventory(cancellationToken, includedItemIds, includedLibraryIds, inventoryProgress, maximumEntries: null)
                .Where(entry => string.IsNullOrEmpty(afterKey)
                    || string.CompareOrdinal(entry.Reference.Key, afterKey) > 0)
                .Take(maximumEntries is > 0 ? maximumEntries.Value + 1 : int.MaxValue)
                .ToArray();
#endif
        }

        public void EnsureIdle(Guid? itemId = null) => inner.EnsureIdle(itemId);

        public ArtworkReference? GetCurrent(ArtworkReference reference)
        {
            GetCurrentCalls++;
            return inner.GetCurrent(reference);
        }

        public string GetDestination(ArtworkReference reference, string mimeType) => inner.GetDestination(reference, mimeType);

        public bool IsPathReferenced(string path) => inner.IsPathReferenced(path);

        public int CountPathReferences(string path)
        {
            CountRefCalls++;
            return inner.CountPathReferences(path);
        }

        public IReadOnlyDictionary<string, int> CountPathReferences(IReadOnlyCollection<string> paths)
        {
            CountRefCalls++;
            return inner.CountPathReferences(paths);
        }

        public bool IsSlotSafeForMutation(ArtworkReference reference)
        {
            SlotSafetyCalls++;
            return inner.IsSlotSafeForMutation(reference);
        }

        public Task<ArtworkReference> SaveAsync(ArtworkReference reference, string stagingPath, string mimeType) =>
            inner.SaveAsync(reference, stagingPath, mimeType);

        public InventorySummary? GetInventorySummary() => inner.GetInventorySummary();

        public IReadOnlyList<ItemArtworkInspection> InspectItems(IReadOnlyCollection<Guid> itemIds, CancellationToken cancellationToken) =>
            inner.InspectItems(itemIds, cancellationToken);
    }

    private sealed class ApplyStore : IArtworkStore
    {
        private readonly string _root;
        private readonly List<Entry> _entries = [];
        private readonly Dictionary<string, Entry> _byKey = new(StringComparer.Ordinal);
        private readonly List<string> _referencePaths = [];

        public ApplyStore(string root) => _root = root;

        public long InventoryCalls { get; private set; }
        public long GetCurrentCalls { get; private set; }
        public long CountRefSingle { get; private set; }
        public long CountRefCollection { get; private set; }
        public long RefScanRows { get; private set; }
        public long SaveCalls { get; private set; }
        public long GetItemIdsCalls => 0;
        public long GetItemListCalls => 0;
        public long ItemsHydrated => 0;

        public void ResetCounters()
        {
            InventoryCalls = 0;
            GetCurrentCalls = 0;
            CountRefSingle = 0;
            CountRefCollection = 0;
            RefScanRows = 0;
            SaveCalls = 0;
        }

        public void AddApplyItems()
        {
            var content = SmallPng();
            for (var index = 0; index < ApplyCount; index++)
            {
                var id = new Guid(1_000_000 + index, 0, 0, new byte[8]);
                var path = Path.Combine(_root, "artwork", id.ToString("N"), "poster.png");
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(path, content);
                var reference = new ArtworkReference(id, ImageType.Primary, 0, path);
                var entry = new Entry
                {
                    Reference = reference,
                    State = "Ready",
                    Name = "Apply synthetic " + index,
                    ItemType = "Movie",
                    Year = 2026,
                };
                _entries.Add(entry);
                _byKey[reference.Key] = entry;
            }
        }

        public void AddReferencePaths(int total)
        {
            foreach (var entry in _entries)
                _referencePaths.Add(Path.GetFullPath(entry.Reference.Path));
            for (var index = _referencePaths.Count; index < total; index++)
                _referencePaths.Add(Path.GetFullPath(Path.Combine(_root, "reference", index.ToString("D6") + ".png")));
            if (_referencePaths.Count != total)
                throw new InvalidOperationException($"Expected {total} reference paths, got {_referencePaths.Count}.");
        }

        public IReadOnlyList<ArtworkReference> Inventory(CancellationToken cancellationToken,
            IReadOnlyCollection<Guid>? includedItemIds = null, IReadOnlyCollection<Guid>? includedLibraryIds = null,
            Action<int>? inventoryProgress = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            InventoryCalls++;
            var references = _entries.Select(entry => entry.Reference).ToArray();
            inventoryProgress?.Invoke(references.Length);
            return references;
        }

        public IReadOnlyList<ArtworkInventoryEntry> DetailedInventory(CancellationToken cancellationToken,
            IReadOnlyCollection<Guid>? includedItemIds = null, IReadOnlyCollection<Guid>? includedLibraryIds = null,
            Action<int>? inventoryProgress = null, int? maximumEntries = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            InventoryCalls++;
            var selected = includedItemIds is { Count: > 0 } ? includedItemIds.ToHashSet() : null;
            var rows = _entries
                .Where(entry => selected is null || selected.Contains(entry.Reference.ItemId))
                .Select(ToEntry)
                .ToArray();
            if (maximumEntries is > 0 and < int.MaxValue)
                rows = rows.Take(maximumEntries.Value + 1).ToArray();
            inventoryProgress?.Invoke(rows.Length);
            return rows;
        }

        public IReadOnlyList<ArtworkInventoryEntry> DetailedInventoryPage(CancellationToken cancellationToken,
            IReadOnlyCollection<Guid>? includedItemIds = null, IReadOnlyCollection<Guid>? includedLibraryIds = null,
            string? afterKey = null, int? maximumEntries = null, Action<int>? inventoryProgress = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            InventoryCalls++;
            var selected = includedItemIds is { Count: > 0 } ? includedItemIds.ToHashSet() : null;
            var rows = _entries
                .Where(entry => selected is null || selected.Contains(entry.Reference.ItemId))
                .Where(entry => string.IsNullOrEmpty(afterKey)
                    || string.CompareOrdinal(entry.Reference.Key, afterKey) > 0)
                .Select(ToEntry)
                .Take(maximumEntries is > 0 ? maximumEntries.Value + 1 : int.MaxValue)
                .ToArray();
            inventoryProgress?.Invoke(rows.Length);
            return rows;
        }

        public void EnsureIdle(Guid? itemId = null)
        {
        }

        public ArtworkReference? GetCurrent(ArtworkReference reference)
        {
            GetCurrentCalls++;
            var entry = _byKey.GetValueOrDefault(reference.Key);
            return entry is null || entry.State is "Remote" or "Missing" ? null : entry.Reference;
        }

        public string GetDestination(ArtworkReference reference, string mimeType) => Path.ChangeExtension(reference.Path,
            mimeType switch
            {
                "image/webp" => ".webp",
                "image/png" => ".png",
                _ => ".jpg",
            });

        public bool IsPathReferenced(string path) => CountPathReferences(path) > 0;

        public int CountPathReferences(string path)
        {
            CountRefSingle++;
            var absolute = Path.GetFullPath(path);
            return Scan([absolute]).GetValueOrDefault(absolute);
        }

        public IReadOnlyDictionary<string, int> CountPathReferences(IReadOnlyCollection<string> paths)
        {
            CountRefCollection++;
            return Scan(paths);
        }

        public bool IsSlotSafeForMutation(ArtworkReference reference) => true;

        public Task<ArtworkReference> SaveAsync(ArtworkReference reference, string stagingPath, string mimeType)
        {
            SaveCalls++;
            var entry = _byKey[reference.Key];
            var destination = GetDestination(reference, mimeType);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.WriteAllBytes(destination, File.ReadAllBytes(stagingPath));
            var previous = entry.Reference.Path;
            if (previous != destination) File.Delete(previous);
            entry.Reference = entry.Reference with { Path = destination };
            return Task.FromResult(entry.Reference);
        }

        private Dictionary<string, int> Scan(IReadOnlyCollection<string> paths)
        {
            var absolute = paths.Select(Path.GetFullPath).ToHashSet(StringComparer.Ordinal);
            var counts = absolute.ToDictionary(path => path, _ => 0, StringComparer.Ordinal);
            if (counts.Count == 0) return counts;
            foreach (var path in _referencePaths)
            {
                if (counts.ContainsKey(path)) counts[path]++;
            }
            RefScanRows += _referencePaths.Count;
            return counts;
        }

        private static ArtworkInventoryEntry ToEntry(Entry entry) => new()
        {
            Reference = entry.Reference,
            Name = entry.Name,
            ItemType = entry.ItemType,
            ProductionYear = entry.Year,
            Category = entry.Reference.Type.ToString(),
            State = entry.State,
            CurrentBytes = File.Exists(entry.Reference.Path) ? new FileInfo(entry.Reference.Path).Length : null,
            MimeType = "image/png",
            IsShared = false,
            ReferenceCount = 1,
        };

        private sealed class Entry
        {
            public ArtworkReference Reference { get; set; } = null!;
            public string State { get; set; } = "Ready";
            public string Name { get; set; } = string.Empty;
            public string ItemType { get; set; } = string.Empty;
            public int? Year { get; set; }
        }
    }

    private static byte[] SmallPng()
    {
        using var bitmap = new SKBitmap(32, 32);
        var random = new Random(42);
        for (var y = 0; y < 32; y++)
        for (var x = 0; x < 32; x++)
            bitmap.SetPixel(x, y, new SKColor((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256), 255));
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        return encoded.ToArray();
    }

    private sealed record Sample(
        double Ms,
        double? AnalysisMs,
        double? ApplyMs,
        long GetItemIds,
        long GetItemList,
        long ItemsHydrated,
        long GetCurrent,
        long CountRefSingle,
        long CountRefCollection,
        long RefScanRows,
        long SaveCalls,
        long JsonFiles,
        long JsonBytes,
        int Rows,
        string? Note);

    private sealed record CaseResult(
        string Name,
        int Runs,
        double MedianMs,
        double MedianAnalysisMs,
        double MedianApplyMs,
        Sample Counts,
        string Note);
}
