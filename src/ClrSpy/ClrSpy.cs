using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Cronos;
using Microsoft.Diagnostics.Runtime;
using Microsoft.Extensions.Logging;

namespace ClrSpy
{
    using ResultsDictionary = Dictionary<(string TypeName, int Gen), uint>;

    public class ClrSpyConfiguration
    {
        public const int MaxGenerations = 5;

        public Func<DateTimeOffset> DatetimeAccessor { get; set; }
        public CronExpression Schedule { get; set; }
        public ClrRuntime Runtime { get; set; }
        public string OutputFilenameTemplate { get; set; }
        public int PrintDiffLimit { get; set; }
        public bool[] GcGenToCollect { get; } = new bool[MaxGenerations];
    }

    public class ClrSpy
    {
        private CancellationTokenSource _stopCancellationTokenSource;
        private ClrSpyConfiguration _configuration;
        private readonly ILogger<ClrSpy> _logger;
        private ResultsDictionary _prevResults;

        public ClrSpy(ClrSpyConfiguration configuration, ILogger<ClrSpy> logger)
        {
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _prevResults = new ResultsDictionary();
        }

        public Task StartAsync(CancellationToken? cancellationToken = null)
        {
            var stopToken = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken ?? new CancellationToken()).Token;
            return Task.Factory.StartNew(async () => {

                var prevRun = _configuration.DatetimeAccessor();
                if (_configuration.Schedule is null) {
                    using (_logger.BeginScope("Processing heap")) {
                        await ProcessHeapAsync(prevRun).ConfigureAwait(false);
                    }
                    return;
                }

                // Task.Delay use Timer + create memory traffic, so we just block thread via CT
                // See https://referencesource.microsoft.com/#mscorlib/system/threading/Tasks/Task.cs,5896
                while (!_stopCancellationTokenSource.Token.WaitHandle.WaitOne(1000)) {
                    var timeSnapshot = _configuration.DatetimeAccessor();
                    var currentRun = _configuration.Schedule.GetNextOccurrence(prevRun,
                        TimeZoneInfo.Local,
                        inclusive: false);

                    if (currentRun.Value < timeSnapshot) {
                        using (_logger.BeginScope("Processing heap")) {
                            _logger.LogDebug("Run processing, time triggered {CurrentRun}. Next processing time: {NextRun}",
                                currentRun,
                                _configuration.Schedule.GetNextOccurrence(timeSnapshot,
                                    TimeZoneInfo.Local,
                                    inclusive: false));

                            await ProcessHeapAsync(timeSnapshot).ConfigureAwait(false);

                            _logger.LogDebug("End processing, for time triggered {CurrentRun}", currentRun);
                        }
                    }

                    // we don't want miss any run
                    prevRun = timeSnapshot;
                }
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap();
        }

        private async ValueTask ProcessHeapAsync(DateTimeOffset timeSnapshot)
        {
            await Task.Yield();

            var runtime = _configuration.Runtime;
            var gcGenToCollect = _configuration.GcGenToCollect;
            var heap = runtime.Heap;

            // GC heap traverse isn't thread safe, as I found (?)
            var results = new ResultsDictionary(16_000);

            foreach (var addr in heap.EnumerateObjectAddresses().Where(a => a != 0)) {
                try {
                    var typeName = heap.GetObjectType(addr)?.Name;
                    if (!string.IsNullOrEmpty(typeName) && typeName != "Free") {
                        int gen = heap.GetGeneration(addr);
                        if (gcGenToCollect[gen]) {
                            var key = (typeName, gen);
                            results[key] = results.TryGetValue(key, out var count) ? count + 1 : 1;
                        }
                    }
                }
                catch (Exception ex) {
                    _logger.LogWarning(ex, "Address '{Address}' can't be readed", addr);
                }
            }
            var printDiffTask = _configuration.PrintDiffLimit >= 0
                ? PrintDiffAsync(results)
                : new ValueTask();

            if (!string.IsNullOrWhiteSpace(_configuration.OutputFilenameTemplate)) {
                await WriteOutputFileAsync(timeSnapshot, results).ConfigureAwait(false);
            }
            await printDiffTask.ConfigureAwait(false);
            Std.Exchange(ref _prevResults, results).Clear();
        }

        private async Task WriteOutputFileAsync(DateTimeOffset timeSnapshot, ResultsDictionary results)
        {
            var sb = StringBuilderCache.Get(1024);
            var datetimeString = timeSnapshot.ToString("s");
            using (var fs = GetOutputStream(timeSnapshot))
            using (var textStream = new StreamWriter(fs, Encoding.UTF8)) {
                var list = results.ToList().OrderByDescending(kv => kv.Value).ToList();
                foreach (var kv in list) {
                    sb.Length = 0;

                    sb.Append(datetimeString).Append("\t")
                        .Append(kv.Key.TypeName).Append("\t")
                        .Append(kv.Key.Gen).Append("\t")
                        .Append(kv.Value).Append("\r\n");

                    // ToDo: use Span / ValueStringBuilder
                    await textStream.WriteAsync(sb.ToString()).ConfigureAwait(false);
                }
                sb.ToStringRecycle();

                await textStream.FlushAsync();
                await fs.FlushAsync();
            }
        }

        // ToDo: optimize this
        private async ValueTask PrintDiffAsync(ResultsDictionary next)
        {
            await Task.Yield();
            var diff = DictionaryDiffComparer.GetDiff(_prevResults, next);
            var enumerable = diff.Select(d => new { Diff = d, Abs = Math.Abs(d.NextValue - d.PrevValue) })
                .Where(d => d.Abs > 0)
                .OrderByDescending(d => d.Abs);

            var diffToPrint = (_configuration.PrintDiffLimit > 0 ? enumerable.Take(_configuration.PrintDiffLimit) : enumerable)
                .ToList();

            if (diffToPrint.Count <= 0)
                return;

            using (new Foreground(ConsoleColor.Yellow)) {
                Console.Write("| {0,-88}|", "Type name");
                Console.Write("Gen");
                Console.WriteLine("|    Count|");

                foreach (var d in diffToPrint) {
                    Console.Write("| ");
                    using (new Foreground(ConsoleColor.Gray)) {
                        Console.Write("{0,-88}", d.Diff.Key.TypeName.Length <= 88
                            ? d.Diff.Key.TypeName
                            : d.Diff.Key.TypeName.Substring(0, 85) + "...");
                    }

                    Console.Write("|");
                    using (new Foreground(ConsoleColor.DarkBlue)) {
                        Console.Write($" {d.Diff.Key.Gen} ");
                    }

                    Console.Write("|");
                    var sub = d.Diff.NextValue - d.Diff.PrevValue;
                    if (sub == 0)
                        using (new Foreground(ConsoleColor.Gray)) {
                            Console.Write(" {0,8:n0}", 0);
                        }
                    else
                        using (new Foreground(sub > 0 ? ConsoleColor.Green : ConsoleColor.Red)) {
                            Console.Write($" {(sub > 0 ? "↑" : "↓")}{d.Abs,7:n0}");
                        }
                    Console.WriteLine("|");
                }
            }
        }
        private Stream GetOutputStream(DateTimeOffset timeSnapshot)
        {
            var path = GetFilePathFromTemplate(timeSnapshot);
            // ToDo: add stream caching
            return new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Delete, 4096, true);
        }

        private string GetFilePathFromTemplate(DateTimeOffset timeSnapshot)
        {
            const string replacement = "_";

            var path = _configuration.OutputFilenameTemplate.Replace("{DateTime}",
                string.Join(replacement, timeSnapshot.ToString("s").Split(Path.GetInvalidFileNameChars())));

            Directory.CreateDirectory(Path.GetDirectoryName(path));
            return path;
        }

    }


}
