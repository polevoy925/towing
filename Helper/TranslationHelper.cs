﻿using CarCareTracker.Models;
using Microsoft.Extensions.Caching.Memory;
using System.Text.Json;

namespace CarCareTracker.Helper
{
    public interface ITranslationHelper
    {
        string Translate(string userLanguage, string text);
        Dictionary<string, string> GetTranslations(string userLanguage);
        OperationResponse SaveTranslation(string userLanguage, Dictionary<string, string> translations);
        string ExportTranslation(Dictionary<string, string> translations);
    }
    public class TranslationHelper : ITranslationHelper
    {
        private readonly IFileHelper _fileHelper;
        private readonly IConfiguration _config;
        private readonly ILogger<ITranslationHelper> _logger;
        private IMemoryCache _cache;
        public TranslationHelper(IFileHelper fileHelper, IConfiguration config, IMemoryCache memoryCache, ILogger<ITranslationHelper> logger)
        {
            _fileHelper = fileHelper;
            _config = config;
            _cache = memoryCache;
            _logger = logger;
        }
        public string Translate(string userLanguage, string text)
        {
            bool create = bool.Parse(_config["LUBELOGGER_TRANSLATOR"] ?? "false");
            //transform input text into key.
            string translationKey = text.Replace(" ", "_");
            var translationFilePath = userLanguage == "en_US" ? _fileHelper.GetFullFilePath($"/defaults/en_US.json") : _fileHelper.GetFullFilePath($"/translations/{userLanguage}.json", false);
            var dictionary = _cache.GetOrCreate<Dictionary<string, string>>($"lang_{userLanguage}", entry =>
            {
                entry.SlidingExpiration = TimeSpan.FromHours(1);
                if (File.Exists(translationFilePath))
                {
                    try
                    {
                        var translationFile = File.ReadAllText(translationFilePath);
                        var translationDictionary = JsonSerializer.Deserialize<Dictionary<string, string>>(translationFile);
                        return translationDictionary ?? new Dictionary<string, string>();
                    } catch (Exception ex)
                    {
                        _logger.LogError(ex.Message);
                        return new Dictionary<string, string>();
                    }
                }
                else
                {
                    _logger.LogError($"Could not find translation file for {userLanguage}");
                    return new Dictionary<string, string>();
                }
            });
            if (dictionary != null && dictionary.ContainsKey(translationKey))
            {
                return dictionary[translationKey];
            }
            else if (create && File.Exists(translationFilePath))
            {
                //create entry
                dictionary.Add(translationKey, text);
                _logger.LogInformation($"Translation key added to {userLanguage} for {translationKey} with value {text}");
                File.WriteAllText(translationFilePath, JsonSerializer.Serialize(dictionary));
                return text;
            }
            return text;
        }
        private Dictionary<string, string> GetDefaultTranslation()
        {
            //this method always returns en_US translation.
            var translationFilePath = _fileHelper.GetFullFilePath($"/defaults/en_US.json");
            if (!string.IsNullOrWhiteSpace(translationFilePath))
            {
                //file exists.
                try
                {
                    var translationFile = File.ReadAllText(translationFilePath);
                    var translationDictionary = JsonSerializer.Deserialize<Dictionary<string, string>>(translationFile);
                    return translationDictionary ?? new Dictionary<string, string>();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex.Message);
                    return new Dictionary<string, string>();
                }
            }
            _logger.LogError($"Could not find translation file for en_US");
            return new Dictionary<string, string>();
        }
        public Dictionary<string, string> GetTranslations(string userLanguage)
        {
            var defaultTranslation = GetDefaultTranslation();
            if (userLanguage == "en_US")
            {
                return defaultTranslation;
            }
            var translationFilePath = _fileHelper.GetFullFilePath($"/translations/{userLanguage}.json");
            if (!string.IsNullOrWhiteSpace(translationFilePath))
            {
                //file exists.
                try
                {
                    var translationFile = File.ReadAllText(translationFilePath);
                    var translationDictionary = JsonSerializer.Deserialize<Dictionary<string, string>>(translationFile);
                    if (translationDictionary != null)
                    {
                        foreach(var translation in translationDictionary)
                        {
                            if (defaultTranslation.ContainsKey(translation.Key))
                            {
                                defaultTranslation[translation.Key] = translation.Value;
                            }
                        }
                    }
                    return defaultTranslation ?? new Dictionary<string, string>();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex.Message);
                    return new Dictionary<string, string>();
                }
            }
            _logger.LogError($"Could not find translation file for {userLanguage}");
            return new Dictionary<string, string>();
        }
        public OperationResponse SaveTranslation(string userLanguage, Dictionary<string, string> translations)
        {
            bool create = bool.Parse(_config["LUBELOGGER_TRANSLATOR"] ?? "false");
            bool isDefaultLanguage = userLanguage == "en_US";
            if (isDefaultLanguage && !create)
            {
                return OperationResponse.Failed("The translation file name en_US is reserved.");
            }
            if (string.IsNullOrWhiteSpace(userLanguage))
            {
                return OperationResponse.Failed("File name is not provided.");
            }
            if (!translations.Any())
            {
                return OperationResponse.Failed("Translation has no data.");
            }
            var translationFilePath = isDefaultLanguage ? _fileHelper.GetFullFilePath($"/defaults/en_US.json") : _fileHelper.GetFullFilePath($"/translations/{userLanguage}.json", false);
            try
            {
                if (File.Exists(translationFilePath))
                {
                    //write to file
                    File.WriteAllText(translationFilePath, JsonSerializer.Serialize(translations));
                    _cache.Remove($"lang_{userLanguage}"); //clear out cache, force a reload from file.
                } else
                {
                    //check if directory exists first.
                    var translationDirectory = _fileHelper.GetFullFilePath("translations/", false);
                    if (!Directory.Exists(translationDirectory))
                    {
                        Directory.CreateDirectory(translationDirectory);
                    }
                    //write to file
                    File.WriteAllText(translationFilePath, JsonSerializer.Serialize(translations));
                }
                return OperationResponse.Succeed("Translation Updated");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message);
                return OperationResponse.Failed();
            }
        }
        public string ExportTranslation(Dictionary<string, string> translations)
        {
            try
            {
                var tempFileName = $"/temp/{Guid.NewGuid()}.json";
                string uploadDirectory = _fileHelper.GetFullFilePath("temp/", false);
                if (!Directory.Exists(uploadDirectory))
                {
                    Directory.CreateDirectory(uploadDirectory);
                }
                var saveFilePath = _fileHelper.GetFullFilePath(tempFileName, false);
                //standardize translation format for export only.
                Dictionary<string, string> sortedTranslations = new Dictionary<string, string>();
                foreach (var translation in translations.OrderBy(x => x.Key))
                {
                    sortedTranslations.Add(translation.Key, translation.Value);
                };
                File.WriteAllText(saveFilePath, JsonSerializer.Serialize(sortedTranslations, new JsonSerializerOptions { WriteIndented = true }));
                return tempFileName;
            } 
            catch(Exception ex)
            {
                _logger.LogError(ex.Message);
                return string.Empty;
            }
        }
    }
}
