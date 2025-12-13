using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;
using Newtonsoft.Json;
using Ntk.Mikrotik.Tools.Models;

namespace Ntk.Mikrotik.Tools.Services
{
    public class JsonDataService
    {
        private readonly string _dataDirectory;

        public JsonDataService()
        {
            _dataDirectory = Path.Combine(Application.StartupPath, "ScanResults");
            if (!Directory.Exists(_dataDirectory))
            {
                Directory.CreateDirectory(_dataDirectory);
            }
        }

        private string? _currentScanFile = null;

        public void SaveScanResults(List<FrequencyScanResult> results, ScanSettings settings)
        {
            var fileName = $"ScanResults_{DateTime.Now:yyyyMMdd_HHmmss}.json";
            var filePath = Path.Combine(_dataDirectory, fileName);
            _currentScanFile = filePath;

            var data = new
            {
                ScanDate = DateTime.Now,
                Settings = settings,
                Results = results
            };

            var json = JsonConvert.SerializeObject(data, Formatting.Indented);
            File.WriteAllText(filePath, json);
        }

        public void SaveSingleResult(FrequencyScanResult result, ScanSettings settings)
        {
            // Use existing file or create new one
            if (string.IsNullOrEmpty(_currentScanFile))
            {
                var fileName = $"ScanResults_{DateTime.Now:yyyyMMdd_HHmmss}.json";
                _currentScanFile = Path.Combine(_dataDirectory, fileName);
            }

            // Load existing results or create new list
            List<FrequencyScanResult> results;
            if (File.Exists(_currentScanFile))
            {
                try
                {
                    var json = File.ReadAllText(_currentScanFile);
                    var data = JsonConvert.DeserializeObject<dynamic>(json);
                    if (data?.Results != null)
                    {
                        results = JsonConvert.DeserializeObject<List<FrequencyScanResult>>(data.Results.ToString()) 
                            ?? new List<FrequencyScanResult>();
                    }
                    else
                    {
                        results = new List<FrequencyScanResult>();
                    }
                }
                catch
                {
                    results = new List<FrequencyScanResult>();
                }
            }
            else
            {
                results = new List<FrequencyScanResult>();
            }

            // Add new result
            results.Add(result);

            // Save back to file
            var dataToSave = new
            {
                ScanDate = DateTime.Now,
                Settings = settings,
                Results = results
            };

            var jsonToSave = JsonConvert.SerializeObject(dataToSave, Formatting.Indented);
            File.WriteAllText(_currentScanFile, jsonToSave);
        }

        public void StartNewScan()
        {
            _currentScanFile = null;
        }

        public List<FrequencyScanResult> LoadScanResults(string filePath)
        {
            if (!File.Exists(filePath))
                return new List<FrequencyScanResult>();

            var json = File.ReadAllText(filePath);
            var data = JsonConvert.DeserializeObject<dynamic>(json);
            
            if (data?.Results != null)
            {
                return JsonConvert.DeserializeObject<List<FrequencyScanResult>>(data.Results.ToString()) 
                    ?? new List<FrequencyScanResult>();
            }

            return new List<FrequencyScanResult>();
        }

        public List<string> GetAvailableScanFiles()
        {
            var files = Directory.GetFiles(_dataDirectory, "ScanResults_*.json");
            return new List<string>(files);
        }
    }
}

