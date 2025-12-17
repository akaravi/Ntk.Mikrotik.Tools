using System;
using System.Collections.Generic;
using System.Linq;
using Ntk.Mikrotik.Tools.Models;

namespace Ntk.Mikrotik.Tools.Services
{
    /// <summary>
    /// سرویس فیلتر کردن نتایج اسکن
    /// این کلاس مسئول اعمال فیلترها بر روی لیست نتایج اسکن است
    /// </summary>
    public class DataFilterService
    {
        /// <summary>
        /// اعمال فیلترها بر روی لیست نتایج
        /// این متد بر اساس فیلترهای ارائه شده، نتایج را فیلتر می‌کند
        /// </summary>
        /// <param name="allResults">لیست کامل نتایج اسکن</param>
        /// <param name="filters">دیکشنری شامل نام property و مقدار فیلتر (مقدار خالی به معنای عدم فیلتر است)</param>
        /// <returns>لیست نتایج فیلتر شده</returns>
        public List<FrequencyScanResult> ApplyFilters(
            List<FrequencyScanResult> allResults, 
            Dictionary<string, string> filters)
        {
            if (allResults == null || filters == null)
            {
                return new List<FrequencyScanResult>();
            }

            try
            {
                var filteredResults = allResults.Where(result =>
                {
                    foreach (var filterPair in filters)
                    {
                        var propertyName = filterPair.Key;
                        var filterText = filterPair.Value?.Trim() ?? "";
                        
                        if (string.IsNullOrEmpty(filterText))
                            continue;

                        var property = typeof(FrequencyScanResult).GetProperty(
                            propertyName, 
                            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                        
                        if (property == null)
                            continue;

                        var value = property.GetValue(result);
                        var valueStr = value?.ToString() ?? "";

                        // Case-insensitive contains search
                        if (!valueStr.Contains(filterText, StringComparison.OrdinalIgnoreCase))
                        {
                            return false;
                        }
                    }
                    return true;
                }).ToList();

                return filteredResults;
            }
            catch (Exception ex)
            {
                // Log error but don't throw - return empty list instead
                System.Diagnostics.Debug.WriteLine($"Error applying filters: {ex.Message}");
                return new List<FrequencyScanResult>();
            }
        }
    }
}

