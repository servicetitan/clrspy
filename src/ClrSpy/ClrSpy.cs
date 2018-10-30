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
    public class ClrSpyConfiguration
    {
        public Func<DateTimeOffset> DatetimeAccessor { get; set; }
        public CronExpression Schedule { get; set; }
        public int Pid { get; set; }
        public string OutputFilenameTemplate { get; set; }
        public int PrintDiffLimit { get; set; }
        public List<int> GcGenToCollect{ get; set; }
    }
    public class ClrSpy
    {
        private CancellationTokenSource _stopCancellationTokenSource;
        private ClrSpyConfiguration _configuration;
        private readonly ILogger<ClrSpy> _logger;
        private Dictionary<(string TypeName, int Gen), int> _prevResults;

        public ClrSpy(ClrSpyConfiguration configuration, ILogger<ClrSpy> logger)
        {
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _prevResults = new Dictionary<(string TypeName, int Gen), int>();
        }

        public Task StartAsync(CancellationToken? cancellationToken = null)
        {
            _stopCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken ?? new CancellationToken());
            return Task.Factory.StartNew(async () => {

                var prevRun = _configuration.DatetimeAccessor();
                if(_configuration.Schedule is null)
                {
                    using (_logger.BeginScope("Processing heap"))
                    {
                        await ProcessHeapAsync(prevRun).ConfigureAwait(false);
                    }
                    return;
                }
                
                while (!_stopCancellationTokenSource.Token.IsCancellationRequested)
                {
                    var timeSnapshot = _configuration.DatetimeAccessor();
                    var currentRun = _configuration.Schedule.GetNextOccurrence(prevRun,
                        TimeZoneInfo.Local,
                        inclusive: false);

                    if (currentRun.Value < timeSnapshot)
                    {
                        using (_logger.BeginScope("Processing heap"))
                        {
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
                    // Task.Delay use Timer + create memory traffic, so we just block thread via CT
                    // See https://referencesource.microsoft.com/#mscorlib/system/threading/Tasks/Task.cs,5896
                    _stopCancellationTokenSource.Token.WaitHandle.WaitOne(1000);
                }
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap();
        }

        private async ValueTask ProcessHeapAsync(DateTimeOffset timeSnapshot)
        {
            await Task.Yield();
            using (var dataTarget = DataTarget.AttachToProcess(_configuration.Pid, 5000, AttachFlag.NonInvasive))
            {

                var runtime = dataTarget.ClrVersions[0].CreateRuntime();
                var heap = runtime.Heap;

                // GC heap traverse isn't thread safe, as I founded (?)
                var results = new Dictionary<(string TypeName, int Gen), int>(16_000);
                
                foreach (var addr in heap.EnumerateObjectAddresses())
                {
                    try
                    {
                        if (addr == 0)
                            continue;

                        var type = heap.GetObjectType(addr);

                        if (string.IsNullOrEmpty(type?.Name) || type.Name == "Free")
                            continue;

                        int gen = heap.GetGeneration(addr);

                        if (!_configuration.GcGenToCollect.Contains(gen))
                            continue;

                        results[(type.Name, gen)] = results.TryGetValue((type.Name, gen), out int count) ? ++count : 1;
                    }
                    catch(Exception ex)
                    {
                        _logger.LogWarning(ex, "Address '{Address}' can't be readed", addr);
                    }
                }
                var printDiffTask = _configuration.PrintDiffLimit >= 0
                    ? PrintDiffAsync(results)
                    : new ValueTask();

                if (!string.IsNullOrWhiteSpace(_configuration.OutputFilenameTemplate))
                {
                    await WriteOutputFileAsync(timeSnapshot, results).ConfigureAwait(false);
                }
                await printDiffTask.ConfigureAwait(false);
                _prevResults.Clear();
                _prevResults = results;

            }
        }

        private async Task WriteOutputFileAsync(DateTimeOffset timeSnapshot, Dictionary<(string TypeName, int Gen), int> results)
        {
            var sb = StringBuilderCache.Get(1024);
            var datetimeString = timeSnapshot.ToString("s");
            using (var fs = GetOutputStream(timeSnapshot))
            using (var textStream = new StreamWriter(fs, Encoding.UTF8))
            {
                var list = results.ToList().OrderByDescending(kv => kv.Value).ToList();
                foreach (var kv in list)
                {
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
        private async ValueTask PrintDiffAsync(Dictionary<(string TypeName, int Gen), int> next)
        {
            await Task.Yield();
            var diff = DictionaryDiffComparer.GetDiff(_prevResults, next);
            var enumerable = diff.Select(d => new { Diff = d, Abs = Math.Abs(d.NextValue - d.PrevValue) })
                .Where(d => d.Abs > 0)
                .OrderByDescending(d => d.Abs);

            var diffToPrint = (_configuration.PrintDiffLimit > 0
                ? enumerable.Take(_configuration.PrintDiffLimit)
                : enumerable).ToList();

            if (diffToPrint.Count <= 0)
                return;

            WriteColored("| {0,-88}|", ConsoleColor.Yellow, "Type name");
            WriteColored("Gen", ConsoleColor.Yellow);
            WriteColored("|    Count|\r\n", ConsoleColor.Yellow);
            


            foreach (var d in diffToPrint)
            {
                WriteColored("| ", ConsoleColor.Yellow);
                WriteColored("{0,-88}", ConsoleColor.Gray, d.Diff.Key.TypeName.Length <= 88
                    ? d.Diff.Key.TypeName
                    : d.Diff.Key.TypeName.Substring(0,85) + "...");
                WriteColored("|", ConsoleColor.Yellow);
                WriteColored(" {0} ", ConsoleColor.DarkBlue, d.Diff.Key.Gen);
                WriteColored("|", ConsoleColor.Yellow);
                var sub = d.Diff.NextValue - d.Diff.PrevValue;
                if (sub > 0)
                    WriteColored(" ↑{0,7:n0}", ConsoleColor.Green, d.Abs);
                else if(sub < 0)
                    WriteColored(" ↓{0,7:n0}", ConsoleColor.Red, d.Abs);
                else
                    WriteColored(" {0,8:n0}", ConsoleColor.Gray, 0);
                WriteColored("|\r\n", ConsoleColor.Yellow);


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

            var dirPath = Path.GetDirectoryName(path);
            if (!Directory.Exists(dirPath))
                Directory.CreateDirectory(dirPath);
            return path;
        }

        private static void WriteColored(string format, ConsoleColor color, params object[] args)
        {
            Console.ForegroundColor = color;
            Console.Write(format, args);
            Console.ResetColor();
        }

    }

    
}
