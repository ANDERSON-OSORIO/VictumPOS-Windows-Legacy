using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace VictumPOS.Services
{
    public class QueueService
    {
        private readonly string _queuePath;
        private readonly PrintService _printService;
        private readonly Timer _timer;
        private readonly object _sync = new object();
        private bool _isProcessing;
        private readonly JavaScriptSerializer _serializer = new JavaScriptSerializer();

        public QueueService(PrintService printService)
        {
            _printService = printService;
            _queuePath = SettingsService.ResolveDataPath("queue.json");
            EnsureQueueFile();
            _timer = new Timer(async _ => await SafeProcess(), null, 2000, 2000);
        }

        public void Enqueue(string content, string printer)
        {
            try
            {
                lock (_sync)
                {
                    var jobs = LoadJobs();
                    var hash = GenerateHash(content, printer);
                    if (jobs.Any(j => j.UniqueHash == hash && j.Status != "error"))
                        return;

                    jobs.Add(new PrintJob
                    {
                        Id = jobs.Count == 0 ? 1 : jobs.Max(j => j.Id) + 1,
                        Content = content,
                        Printer = printer,
                        UniqueHash = hash,
                        Status = "pending",
                        Attempts = 0,
                        MaxAttempts = 5,
                        CreatedAt = DateTime.UtcNow
                    });
                    SaveJobs(jobs);
                }
            }
            catch (Exception ex)
            {
                Logger.Log("No se pudo encolar impresion: " + ex.Message);
            }
        }

        private async Task SafeProcess()
        {
            if (_isProcessing)
                return;

            _isProcessing = true;
            try
            {
                await ProcessQueue();
            }
            finally
            {
                _isProcessing = false;
            }
        }

        private async Task ProcessQueue()
        {
            var jobs = LockJobs(10);
            foreach (var job in jobs)
            {
                try
                {
                    await _printService.PrintFromQueue(job.Content, job.Printer);
                    MarkPrinted(job.Id);
                }
                catch (Exception ex)
                {
                    HandleError(job, ex.Message);
                }

                await Task.Delay(200);
            }
        }

        private List<PrintJob> LockJobs(int limit)
        {
            lock (_sync)
            {
                var now = DateTime.UtcNow;
                var jobs = LoadJobs();
                var selected = jobs
                    .Where(j => j.Status == "pending" && (!j.NextRunAt.HasValue || j.NextRunAt.Value <= now))
                    .OrderBy(j => j.Id)
                    .Take(limit)
                    .ToList();

                foreach (var job in selected)
                    job.Status = "processing";

                SaveJobs(jobs);
                return selected;
            }
        }

        private void MarkPrinted(int id)
        {
            lock (_sync)
            {
                var jobs = LoadJobs();
                var job = jobs.FirstOrDefault(j => j.Id == id);
                if (job == null)
                    return;

                job.Status = "printed";
                job.PrintedAt = DateTime.UtcNow;
                SaveJobs(jobs);
            }
        }

        private void HandleError(PrintJob job, string error)
        {
            lock (_sync)
            {
                var jobs = LoadJobs();
                var existing = jobs.FirstOrDefault(j => j.Id == job.Id);
                if (existing == null)
                    return;

                existing.Attempts++;
                existing.ErrorMessage = error;

                if (existing.Attempts >= existing.MaxAttempts)
                    existing.Status = "error";
                else
                {
                    existing.Status = "pending";
                    existing.NextRunAt = DateTime.UtcNow.AddSeconds(Math.Pow(2, existing.Attempts));
                }

                SaveJobs(jobs);
            }
        }

        private void EnsureQueueFile()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_queuePath));
            if (!File.Exists(_queuePath))
                File.WriteAllText(_queuePath, "[]");
        }

        private List<PrintJob> LoadJobs()
        {
            EnsureQueueFile();
            var json = File.ReadAllText(_queuePath);
            return _serializer.Deserialize<List<PrintJob>>(json) ?? new List<PrintJob>();
        }

        private void SaveJobs(List<PrintJob> jobs)
        {
            File.WriteAllText(_queuePath, _serializer.Serialize(jobs));
        }

        private string GenerateHash(string content, string printer)
        {
            using (var sha = SHA1.Create())
            {
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes((content ?? "") + "-" + (printer ?? "")));
                return BitConverter.ToString(bytes).Replace("-", "");
            }
        }
    }

    public class PrintJob
    {
        public int Id { get; set; }
        public string Content { get; set; }
        public string Printer { get; set; }
        public string Status { get; set; }
        public int Attempts { get; set; }
        public int MaxAttempts { get; set; }
        public DateTime? NextRunAt { get; set; }
        public string UniqueHash { get; set; }
        public string ErrorMessage { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? PrintedAt { get; set; }
    }
}
