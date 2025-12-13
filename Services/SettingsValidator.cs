using System;
using System.Collections.Generic;
using Ntk.Mikrotik.Tools.Models;

namespace Ntk.Mikrotik.Tools.Services
{
    public static class SettingsValidator
    {
        public static ValidationResult Validate(ScanSettings settings)
        {
            var errors = new List<string>();

            if (string.IsNullOrWhiteSpace(settings.RouterIpAddress))
            {
                errors.Add("آدرس IP روتر نمی‌تواند خالی باشد.");
            }
            else if (!System.Net.IPAddress.TryParse(settings.RouterIpAddress, out _))
            {
                errors.Add("آدرس IP معتبر نیست.");
            }

            if (settings.SshPort < 1 || settings.SshPort > 65535)
            {
                errors.Add("پورت SSH باید بین 1 تا 65535 باشد.");
            }

            if (string.IsNullOrWhiteSpace(settings.Username))
            {
                errors.Add("نام کاربری نمی‌تواند خالی باشد.");
            }

            if (string.IsNullOrWhiteSpace(settings.Password))
            {
                errors.Add("رمز عبور نمی‌تواند خالی باشد.");
            }

            if (settings.StartFrequency < 1000 || settings.StartFrequency > 6000)
            {
                errors.Add("فرکانس شروع باید بین 1000 تا 6000 MHz باشد.");
            }

            if (settings.EndFrequency < 1000 || settings.EndFrequency > 6000)
            {
                errors.Add("فرکانس پایان باید بین 1000 تا 6000 MHz باشد.");
            }

            if (settings.StartFrequency >= settings.EndFrequency)
            {
                errors.Add("فرکانس شروع باید کمتر از فرکانس پایان باشد.");
            }

            if (settings.FrequencyStep <= 0)
            {
                errors.Add("پرش فرکانس باید بیشتر از صفر باشد.");
            }

            if (settings.FrequencyStep > (settings.EndFrequency - settings.StartFrequency))
            {
                errors.Add("پرش فرکانس نمی‌تواند بیشتر از تفاوت فرکانس شروع و پایان باشد.");
            }

            if (settings.StabilizationTimeMinutes < 1 || settings.StabilizationTimeMinutes > 60)
            {
                errors.Add("زمان استیبل شدن باید بین 1 تا 60 دقیقه باشد.");
            }

            if (string.IsNullOrWhiteSpace(settings.InterfaceName))
            {
                errors.Add("نام اینترفیس نمی‌تواند خالی باشد.");
            }

            return new ValidationResult
            {
                IsValid = errors.Count == 0,
                Errors = errors
            };
        }
    }

    public class ValidationResult
    {
        public bool IsValid { get; set; }
        public List<string> Errors { get; set; } = new List<string>();
    }
}

