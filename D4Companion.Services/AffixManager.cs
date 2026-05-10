using CommunityToolkit.Mvvm.Messaging;
using D4Companion.Entities;
using D4Companion.Helpers;
using D4Companion.Interfaces;
using D4Companion.Messages;
using Microsoft.Extensions.Logging;
using System.IO;
using System.Text.Json;
using System.Windows.Media;

namespace D4Companion.Services
{
    public class AffixManager : IAffixManager
    {
        private readonly ILogger _logger;
        private readonly ISettingsManager _settingsManager;

        private List<AffixInfo> _affixes = new List<AffixInfo>();
        private Dictionary<string, AffixInfo> _affixesByIdName = new Dictionary<string, AffixInfo>();
        private Dictionary<string, AffixInfo> _affixesByIdSno = new Dictionary<string, AffixInfo>();
        private Dictionary<string, AffixInfo> _affixesByIdSnoList = new Dictionary<string, AffixInfo>(); // Flattened lookup for IdSnoList
        private Dictionary<string, AffixInfo> _affixesByIdNameList = new Dictionary<string, AffixInfo>(); // Flattened lookup for IdNameList
        private List<AffixPreset> _affixPresets = new List<AffixPreset>();
        private Dictionary<string, AffixPreset> _affixPresetsByName = new Dictionary<string, AffixPreset>();
        private List<AspectInfo> _aspects = new List<AspectInfo>();
        private Dictionary<string, AspectInfo> _aspectsByIdName = new Dictionary<string, AspectInfo>();
        private Dictionary<string, AspectInfo> _aspectsByIdSnoList = new Dictionary<string, AspectInfo>(); // Flattened lookup for IdSnoList
        private Dictionary<string, AspectInfo> _aspectsByIdNameList = new Dictionary<string, AspectInfo>(); // Flattened lookup for IdNameList
        private List<SigilInfo> _sigils = new List<SigilInfo>();
        private Dictionary<string, SigilInfo> _sigilsByIdName = new Dictionary<string, SigilInfo>();
        private List<UniqueInfo> _uniques = new List<UniqueInfo>();
        private Dictionary<string, UniqueInfo> _uniquesByIdName = new Dictionary<string, UniqueInfo>();
        private Dictionary<string, UniqueInfo> _uniquesByIdSno = new Dictionary<string, UniqueInfo>(); // Flattened lookup for IdSno
        private List<RuneInfo> _runes = new List<RuneInfo>();
        private Dictionary<string, RuneInfo> _runesByIdName = new Dictionary<string, RuneInfo>();
        private List<ParagonBoardInfo> _paragonBoards = new List<ParagonBoardInfo>();
        private Dictionary<string, ParagonBoardInfo> _paragonBoardsByIdName = new Dictionary<string, ParagonBoardInfo>();
        private List<ParagonGlyphInfo> _paragonGlyphs = new List<ParagonGlyphInfo>();
        private Dictionary<string, ParagonGlyphInfo> _paragonGlyphsByIdName = new Dictionary<string, ParagonGlyphInfo>();
        private Dictionary<string, double> _minimalAffixValues = new Dictionary<string, double>(); // <affixId, minimalAffixValue>
        private Dictionary<string, string> _sigilDungeonTiers = new Dictionary<string, string>(); // <sigilId, tier>

        // Start of Constructors region

        #region Constructors

        public AffixManager(ILogger<AffixManager> logger, ISettingsManager settingsManager)
        {
            // Init services
            _logger = logger;
            _settingsManager = settingsManager;

            // Init messages
            WeakReferenceMessenger.Default.Register<AffixLanguageChangedMessage>(this, HandleAffixLanguageChangedMessage);
            WeakReferenceMessenger.Default.Register<ApplicationLoadedMessage>(this, HandleApplicationLoadedMessage);

            // Init store data
            InitAffixData();
            InitAffixMinimalValueData();
            InitAspectData();
            InitSigilData();
            InitSigilDungeonTierData();
            InitUniqueData();
            InitRuneData();
            InitParagonBoardData();
            InitParagonGlyphData();

            // Load affix presets
            LoadAffixPresets();
        }       

        #endregion

        // Start of Events region

        #region Events

        #endregion

        // Start of Properties region

        #region Properties

        public List<AffixInfo> Affixes { get => _affixes; set => _affixes = value; }
        public List<AffixPreset> AffixPresets { get => _affixPresets; }
        public List<AspectInfo> Aspects { get => _aspects; set => _aspects = value; }
        public List<SigilInfo> Sigils { get => _sigils; set => _sigils = value; }
        public List<UniqueInfo> Uniques { get => _uniques; set => _uniques = value; }
        public List<RuneInfo> Runes { get => _runes; set => _runes = value; }
        public List<ParagonBoardInfo> ParagonBoards { get => _paragonBoards; set => _paragonBoards = value; }
        public List<ParagonGlyphInfo> ParagonGlyphs { get => _paragonGlyphs; set => _paragonGlyphs = value; }

        #endregion

        // Start of Event handlers region

        #region Event handlers

        private void HandleAffixLanguageChangedMessage(object recipient, AffixLanguageChangedMessage message)
        {
            InitAffixData();
            InitAspectData();
            InitSigilData();
            InitUniqueData();
            InitRuneData();
            InitParagonBoardData();
            InitParagonGlyphData();

            ValidateAffixPresets();
        }

        private void HandleApplicationLoadedMessage(object recipient, ApplicationLoadedMessage message)
        {
            ValidateAffixPresets();
            ValidateMultiBuild();
        }

        #endregion

        // Start of Methods region

        #region Methods

        public void AddAffixPreset(AffixPreset affixPreset)
        {
            _affixPresets.Add(affixPreset);

            // Update dictionary
            if (!string.IsNullOrEmpty(affixPreset.Name))
            {
                _affixPresetsByName[affixPreset.Name] = affixPreset;
            }

            // Sort list
            _affixPresets.Sort((x, y) =>
            {
                return string.Compare(x.Name, y.Name, StringComparison.Ordinal);
            });

            ValidateAffixPresets();

            WeakReferenceMessenger.Default.Send(new AffixPresetAddedMessage());
        }

        public void RemoveAffixPreset(AffixPreset affixPreset)
        {
            if (affixPreset == null) return;

            _affixPresets.Remove(affixPreset);

            // Update dictionary
            if (!string.IsNullOrEmpty(affixPreset.Name))
            {
                _affixPresetsByName.Remove(affixPreset.Name);
            }

            // Sort list
            _affixPresets.Sort((x, y) =>
            {
                return string.Compare(x.Name, y.Name, StringComparison.Ordinal);
            });

            SaveAffixPresets();
            ValidateMultiBuild();

            WeakReferenceMessenger.Default.Send(new AffixPresetRemovedMessage());
        }

        public void AddAffix(AffixInfo affixInfo, string itemType)
        {
            if (!_affixPresetsByName.TryGetValue(_settingsManager.Settings.SelectedAffixPreset, out var preset))
                return;

            preset.ItemAffixes.Add(new ItemAffix
            {
                Id = affixInfo.IdName,
                Type = itemType,
                Color = _settingsManager.Settings.DefaultColorNormal
            });
            SaveAffixPresets();

            WeakReferenceMessenger.Default.Send(new SelectedAffixesChangedMessage());
        }

        public void RemoveAffix(ItemAffix itemAffix)
        {
            if (!_affixPresetsByName.TryGetValue(_settingsManager.Settings.SelectedAffixPreset, out var preset))
                return;

            var affix = preset.ItemAffixes.FirstOrDefault(a => a.Id.Equals(itemAffix.Id) && a.Type.Equals(itemAffix.Type) &&
                a.IsImplicit == itemAffix.IsImplicit && a.IsGreater == itemAffix.IsGreater && a.IsTempered == itemAffix.IsTempered);
            if (affix == null) return;
            
            preset.ItemAffixes.Remove(affix);
            SaveAffixPresets();

            WeakReferenceMessenger.Default.Send(new SelectedAffixesChangedMessage());
        }

        public void AddAspect(AspectInfo aspectInfo, string itemType)
        {
            if (!_affixPresetsByName.TryGetValue(_settingsManager.Settings.SelectedAffixPreset, out var preset))
                return;

            if (!preset.ItemAspects.Any(a => a.Id.Equals(aspectInfo.IdName) && a.Type.Equals(itemType)))
            {
                preset.ItemAspects.Add(new ItemAffix
                {
                    Id = aspectInfo.IdName,
                    Type = itemType,
                    Color = _settingsManager.Settings.DefaultColorAspects
                });
                SaveAffixPresets();
            }

            WeakReferenceMessenger.Default.Send(new SelectedAspectsChangedMessage());
        }

        public void RemoveAspect(ItemAffix itemAffix)
        {
            if (!_affixPresetsByName.TryGetValue(_settingsManager.Settings.SelectedAffixPreset, out var preset))
                return;

            if (preset.ItemAspects.RemoveAll(a => a.Id.Equals(itemAffix.Id)) > 0)
            {
                SaveAffixPresets();
            }

            WeakReferenceMessenger.Default.Send(new SelectedAspectsChangedMessage());
        }

        public void AddSigil(SigilInfo sigilInfo, string itemType)
        {
            if (!_affixPresetsByName.TryGetValue(_settingsManager.Settings.SelectedAffixPreset, out var preset))
                return;

            if (!preset.ItemSigils.Any(a => a.Id.Equals(sigilInfo.IdName) && a.Type.Equals(itemType)))
            {
                preset.ItemSigils.Add(new ItemAffix
                {
                    Id = sigilInfo.IdName,
                    Type = itemType,
                    Color = _settingsManager.Settings.SelectedSigilDisplayMode.Equals("Whitelisting") ? _settingsManager.Settings.DefaultColorNormal : Colors.Red
                });
                SaveAffixPresets();
            }

            WeakReferenceMessenger.Default.Send(new SelectedSigilsChangedMessage());
        }

        public void RemoveSigil(ItemAffix itemAffix)
        {
            if (!_affixPresetsByName.TryGetValue(_settingsManager.Settings.SelectedAffixPreset, out var preset))
                return;

            if (preset.ItemSigils.RemoveAll(a => a.Id.Equals(itemAffix.Id)) > 0)
            {
                SaveAffixPresets();
            }

            WeakReferenceMessenger.Default.Send(new SelectedSigilsChangedMessage());
        }

        public void AddUnique(UniqueInfo uniqueInfo)
        {
            if (!_affixPresetsByName.TryGetValue(_settingsManager.Settings.SelectedAffixPreset, out var preset))
                return;

            if (!preset.ItemUniques.Any(a => a.Id.Equals(uniqueInfo.IdName)))
            {
                preset.ItemUniques.Add(new ItemAffix
                {
                    Id = uniqueInfo.IdName,
                    Color = _settingsManager.Settings.DefaultColorUniques
                });
                SaveAffixPresets();
            }

            WeakReferenceMessenger.Default.Send(new SelectedUniquesChangedMessage());
        }

        public void RemoveUnique(ItemAffix itemAffix)
        {
            if (!_affixPresetsByName.TryGetValue(_settingsManager.Settings.SelectedAffixPreset, out var preset))
                return;

            if (preset.ItemUniques.RemoveAll(a => a.Id.Equals(itemAffix.Id)) > 0)
            {
                SaveAffixPresets();
            }

            WeakReferenceMessenger.Default.Send(new SelectedUniquesChangedMessage());
        }

        public void AddRune(RuneInfo runeInfo)
        {
            if (!_affixPresetsByName.TryGetValue(_settingsManager.Settings.SelectedAffixPreset, out var preset))
                return;

            if (!preset.ItemRunes.Any(a => a.Id.Equals(runeInfo.IdName)))
            {
                preset.ItemRunes.Add(new ItemAffix
                {
                    Id = runeInfo.IdName,
                    Color = _settingsManager.Settings.DefaultColorRunes
                });
                SaveAffixPresets();
            }

            WeakReferenceMessenger.Default.Send(new SelectedRunesChangedMessage());
        }

        public void RemoveRune(ItemAffix itemAffix)
        {
            if (!_affixPresetsByName.TryGetValue(_settingsManager.Settings.SelectedAffixPreset, out var preset))
                return;

            if (preset.ItemRunes.RemoveAll(a => a.Id.Equals(itemAffix.Id)) > 0)
            {
                SaveAffixPresets();
            }

            WeakReferenceMessenger.Default.Send(new SelectedRunesChangedMessage());
        }

        private void InitAffixData()
        {
            string language = _settingsManager.Settings.SelectedAffixLanguage;

            _affixes.Clear();
            _affixesByIdName.Clear();
            _affixesByIdSno.Clear();
            _affixesByIdSnoList.Clear();
            _affixesByIdNameList.Clear();

            string resourcePath = @".\Data\Affixes.{language}.json";
            using (FileStream? stream = File.OpenRead(resourcePath))
            {
                if (stream != null)
                {
                    // create the options
                    var options = new JsonSerializerOptions()
                    {
                        WriteIndented = true
                    };
                    // register the converter
                    options.Converters.Add(new BoolConverter());
                    options.Converters.Add(new IntConverter());

                    _affixes = JsonSerializer.Deserialize<List<AffixInfo>>(stream, options) ?? new List<AffixInfo>();

                    // Build lookup dictionaries for O(1) access
                    foreach (var affix in _affixes)
                    {
                        if (!string.IsNullOrEmpty(affix.IdName))
                        {
                            _affixesByIdName[affix.IdName] = affix;
                        }
                        if (!string.IsNullOrEmpty(affix.IdSno))
                        {
                            _affixesByIdSno[affix.IdSno] = affix;
                        }
                        foreach (var idSno in affix.IdSnoList)
                        {
                            if (!string.IsNullOrEmpty(idSno))
                            {
                                _affixesByIdSnoList[idSno] = affix;
                            }
                        }
                        foreach (var idName in affix.IdNameList)
                        {
                            if (!string.IsNullOrEmpty(idName))
                            {
                                _affixesByIdNameList[idName] = affix;
                            }
                        }
                    }
                }
            }
        }

        private void InitAffixMinimalValueData()
        {
            try
            {
                _minimalAffixValues.Clear();
                string resourcePath = @$".\Config\MinimalAffixValues.json";
                using (FileStream? stream = File.OpenRead(resourcePath))
                {
                    if (stream != null)
                    {
                        // create the options
                        var options = new JsonSerializerOptions()
                        {
                            WriteIndented = true
                        };
                        // register the converter
                        options.Converters.Add(new BoolConverter());
                        options.Converters.Add(new IntConverter());

                        _minimalAffixValues = JsonSerializer.Deserialize<Dictionary<string, double>>(stream, options) ?? new Dictionary<string, double> { };
                    }
                }
            }
            catch { }
        }

        private void InitAspectData()
        {
            string language = _settingsManager.Settings.SelectedAffixLanguage;

            _aspects.Clear();
            _aspectsByIdName.Clear();
            _aspectsByIdSnoList.Clear();
            _aspectsByIdNameList.Clear();

            string resourcePath = @".\Data\Aspects.{language}.json";
            using (FileStream? stream = File.OpenRead(resourcePath))
            {
                if (stream != null)
                {
                    // create the options
                    var options = new JsonSerializerOptions()
                    {
                        WriteIndented = true
                    };
                    // register the converter
                    options.Converters.Add(new BoolConverter());
                    options.Converters.Add(new IntConverter());

                    _aspects = JsonSerializer.Deserialize<List<AspectInfo>>(stream, options) ?? new List<AspectInfo>();

                    // Build lookup dictionaries for O(1) access
                    foreach (var aspect in _aspects)
                    {
                        if (!string.IsNullOrEmpty(aspect.IdName))
                        {
                            _aspectsByIdName[aspect.IdName] = aspect;
                        }
                        foreach (var idSno in aspect.IdSnoList)
                        {
                            if (!string.IsNullOrEmpty(idSno))
                            {
                                _aspectsByIdSnoList[idSno] = aspect;
                            }
                        }
                        foreach (var idName in aspect.IdNameList)
                        {
                            if (!string.IsNullOrEmpty(idName))
                            {
                                _aspectsByIdNameList[idName] = aspect;
                            }
                        }
                    }
                }
            }
        }

        private void InitSigilData()
        {
            string language = _settingsManager.Settings.SelectedAffixLanguage;

            _sigils.Clear();
            _sigilsByIdName.Clear();

            string resourcePath = @".\Data\Sigils.{language}.json";
            using (FileStream? stream = File.OpenRead(resourcePath))
            {
                if (stream != null)
                {
                    // create the options
                    var options = new JsonSerializerOptions()
                    {
                        WriteIndented = true
                    };
                    // register the converter
                    options.Converters.Add(new BoolConverter());
                    options.Converters.Add(new IntConverter());

                    _sigils = JsonSerializer.Deserialize<List<SigilInfo>>(stream, options) ?? new List<SigilInfo>();

                    // Build lookup dictionary for O(1) access
                    foreach (var sigil in _sigils)
                    {
                        if (!string.IsNullOrEmpty(sigil.IdName))
                        {
                            _sigilsByIdName[sigil.IdName] = sigil;
                        }
                    }
                }
            }
        }

        private void InitSigilDungeonTierData()
        {
            try
            {
                _sigilDungeonTiers.Clear();
                string resourcePath = @$".\Config\DungeonTiers.json";
                using (FileStream? stream = File.OpenRead(resourcePath))
                {
                    if (stream != null)
                    {
                        // create the options
                        var options = new JsonSerializerOptions()
                        {
                            WriteIndented = true
                        };
                        // register the converter
                        options.Converters.Add(new BoolConverter());
                        options.Converters.Add(new IntConverter());

                        _sigilDungeonTiers = JsonSerializer.Deserialize<Dictionary<string, string>>(stream, options) ?? new Dictionary<string, string> { };
                    }
                }
            }
            catch { }
        }

        private void InitUniqueData()
        {
            string language = _settingsManager.Settings.SelectedAffixLanguage;

            _uniques.Clear();
            _uniquesByIdName.Clear();
            _uniquesByIdSno.Clear();

            string resourcePath = @$".\Data\Uniques.{language}.json";
            using (FileStream? stream = File.OpenRead(resourcePath))
            {
                if (stream != null)
                {
                    // create the options
                    var options = new JsonSerializerOptions()
                    {
                        WriteIndented = true
                    };
                    // register the converter
                    options.Converters.Add(new BoolConverter());
                    options.Converters.Add(new IntConverter());

                    _uniques = JsonSerializer.Deserialize<List<UniqueInfo>>(stream, options) ?? new List<UniqueInfo>();

                    // Build lookup dictionaries for O(1) access
                    foreach (var unique in _uniques)
                    {
                        if (!string.IsNullOrEmpty(unique.IdName))
                        {
                            _uniquesByIdName[unique.IdName] = unique;
                        }
                        foreach (var idSno in unique.IdSnoList)
                        {
                            if (!string.IsNullOrEmpty(idSno))
                            {
                                _uniquesByIdSno[idSno] = unique;
                            }
                        }
                    }
                }
            }
        }

        private void InitRuneData()
        {
            string language = _settingsManager.Settings.SelectedAffixLanguage;

            _runes.Clear();
            _runesByIdName.Clear();

            string resourcePath = @$".\Data\Runes.{language}.json";
            using (FileStream? stream = File.OpenRead(resourcePath))
            {
                if (stream != null)
                {
                    // create the options
                    var options = new JsonSerializerOptions()
                    {
                        WriteIndented = true
                    };
                    // register the converter
                    options.Converters.Add(new BoolConverter());
                    options.Converters.Add(new IntConverter());

                    _runes = JsonSerializer.Deserialize<List<RuneInfo>>(stream, options) ?? new List<RuneInfo>();

                    // Build lookup dictionary for O(1) access
                    foreach (var rune in _runes)
                    {
                        if (!string.IsNullOrEmpty(rune.IdName))
                        {
                            _runesByIdName[rune.IdName] = rune;
                        }
                    }
                }
            }
        }

        private void InitParagonBoardData()
        {
            string language = _settingsManager.Settings.SelectedAffixLanguage;

            _paragonBoards.Clear();
            _paragonBoardsByIdName.Clear();

            string resourcePath = @$".\Data\ParagonBoards.{language}.json";
            using (FileStream? stream = File.OpenRead(resourcePath))
            {
                if (stream != null)
                {
                    // create the options
                    var options = new JsonSerializerOptions()
                    {
                        WriteIndented = true
                    };
                    // register the converter
                    options.Converters.Add(new BoolConverter());
                    options.Converters.Add(new IntConverter());

                    _paragonBoards = JsonSerializer.Deserialize<List<ParagonBoardInfo>>(stream, options) ?? new List<ParagonBoardInfo>();

                    // Build lookup dictionary for O(1) access
                    foreach (var board in _paragonBoards)
                    {
                        if (!string.IsNullOrEmpty(board.IdName))
                        {
                            _paragonBoardsByIdName[board.IdName] = board;
                        }
                    }
                }
            }
        }

        private void InitParagonGlyphData()
        {
            string language = _settingsManager.Settings.SelectedAffixLanguage;

            _paragonGlyphs.Clear();
            _paragonGlyphsByIdName.Clear();

            string resourcePath = @$".\Data\ParagonGlyphs.{language}.json";
            using (FileStream? stream = File.OpenRead(resourcePath))
            {
                if (stream != null)
                {
                    // create the options
                    var options = new JsonSerializerOptions()
                    {
                        WriteIndented = true
                    };
                    // register the converter
                    options.Converters.Add(new BoolConverter());
                    options.Converters.Add(new IntConverter());

                    _paragonGlyphs = JsonSerializer.Deserialize<List<ParagonGlyphInfo>>(stream, options) ?? new List<ParagonGlyphInfo>();

                    // Build lookup dictionary for O(1) access
                    foreach (var glyph in _paragonGlyphs)
                    {
                        if (!string.IsNullOrEmpty(glyph.IdName))
                        {
                            _paragonGlyphsByIdName[glyph.IdName] = glyph;
                        }
                    }
                }
            }
        }

        public ItemAffix GetAffix(string affixId, string affixType, string itemType)
        {
            var affixDefault = new ItemAffix
            {
                Id = affixId,
                Type = itemType,
                Color = Colors.Red
            };

            if (!_affixPresetsByName.TryGetValue(_settingsManager.Settings.SelectedAffixPreset, out var preset))
                return affixDefault;

            bool isGreater = affixType.Equals(Constants.AffixTypeConstants.Greater);
            bool isImplicit = affixType.Equals(Constants.AffixTypeConstants.Implicit);
            bool isTempered = affixType.Equals(Constants.AffixTypeConstants.Tempered);

            // Since season 11 a tempered affix can become a greater affix.
            var affix = preset.ItemAffixes.FirstOrDefault(a => a.Id.Equals(affixId) && a.Type.Equals(itemType) && a.IsImplicit == isImplicit && 
                (a.IsTempered == isTempered || (a.IsTempered && isGreater)));

            // Check if the affix is set to accept any item type.
            if (affix == null)
            {
                affix = preset.ItemAffixes.FirstOrDefault(a => a.Id.Equals(affixId));
                affix = affix?.IsAnyType ?? false ? affix : null;
            }

            if (affix == null) return affixDefault;
            return affix;
        }

        public string GetAffixDescription(string affixId)
        {
            if (_affixesByIdName.TryGetValue(affixId, out var affixInfo))
            {
                return affixInfo.Description;
            }
            return string.Empty;
        }

        public string GetAffixId(string affixSno)
        {
            if (_affixesByIdSno.TryGetValue(affixSno, out var affixInfo))
            {
                return affixInfo.IdName;
            }
            return string.Empty;
        }

        /// <summary>
        /// Find Affix with matching sno for affixes used by imported Maxroll builds.
        /// Uses an affix list contaning all known affixes, included affixes with duplicated descriptions.
        /// </summary>
        /// <param name="affixIdSno"></param>
        /// <returns></returns>
        public AffixInfo? GetAffixInfoMaxrollByIdSno(string affixIdSno)
        {
            _affixesByIdSnoList.TryGetValue(affixIdSno, out var affixInfo);
            return affixInfo;
        }

        /// <summary>
        /// Find Affix with matching name for affixes used by imported D2Core and Maxroll builds.
        /// Uses an affix list contaning all known affixes, included affixes with duplicated descriptions.
        /// </summary>
        /// <param name="affixIdName"></param>
        /// <returns></returns>
        public AffixInfo? GetAffixInfoByIdName(string affixIdName)
        {
            _affixesByIdNameList.TryGetValue(affixIdName, out var affixInfo);
            return affixInfo;
        }

        public double GetAffixMinimalValue(string idName)
        {
            return _minimalAffixValues.TryGetValue(idName, out var minimalValue) ? minimalValue : 0;
        }

        public ItemAffix GetAspect(string aspectId, string itemType)
        {
            var affixDefault = new ItemAffix
            {
                Id = aspectId,
                Type = itemType,
                Color = Colors.Red
            };

            if (!_affixPresetsByName.TryGetValue(_settingsManager.Settings.SelectedAffixPreset, out var preset))
                return affixDefault;

            var aspect = preset.ItemAspects.FirstOrDefault(a => a.Id.Equals(aspectId) && a.Type.Equals(itemType));
            if (aspect == null) return affixDefault;
            return aspect;
        }

        public string GetAspectDescription(string aspectId)
        {
            if (_aspectsByIdName.TryGetValue(aspectId, out var aspectInfo))
            {
                return aspectInfo.Description;
            }
            return string.Empty;
        }

        //public string GetAspectId(int aspectSno)
        //{
        //    var aspectInfo = _aspects.FirstOrDefault(a => a.IdSno.Equals(aspectSno));
        //    if (aspectInfo != null)
        //    {
        //        return aspectInfo.IdName;
        //    }
        //    else
        //    {
        //        return string.Empty;
        //    }
        //}

        public string GetAspectName(string aspectId)
        {
            if (_aspectsByIdName.TryGetValue(aspectId, out var aspectInfo))
            {
                return aspectInfo.Name;
            }
            return string.Empty;
        }

        /// <summary>
        /// Find Aspect with matching sno for aspects used by imported Maxroll builds.
        /// Uses an aspect list containing all known aspects, included aspects with duplicated descriptions.
        /// </summary>
        /// <param name="aspectIdSno"></param>
        /// <returns></returns>
        public AspectInfo? GetAspectInfoMaxrollByIdSno(string aspectIdSno)
        {
            _aspectsByIdSnoList.TryGetValue(aspectIdSno, out var aspectInfo);
            return aspectInfo;
        }

        /// <summary>
        /// Find Aspect with matching name for aspects used by imported Maxroll builds.
        /// Uses an aspect list contaning all known aspects, included aspects with duplicated descriptions.
        /// </summary>
        /// <param name="aspectIdName"></param>
        /// <returns></returns>
        public AspectInfo? GetAspectInfoMaxrollByIdName(string aspectIdName)
        {
            _aspectsByIdNameList.TryGetValue(aspectIdName, out var aspectInfo);
            return aspectInfo;
        }

        public string GetParagonBoardLocalisation(string id)
        {
            // Try case-sensitive lookup first, then case-insensitive fallback
            if (_paragonBoardsByIdName.TryGetValue(id, out var board))
            {
                return board.Name;
            }
            // Fallback to case-insensitive search for edge cases
            board = _paragonBoards.FirstOrDefault(b => b.IdName.Equals(id, StringComparison.OrdinalIgnoreCase));
            return board?.Name ?? id;
        }

        public string GetParagonGlyphLocalisation(string id)
        {
            // Try case-sensitive lookup first, then case-insensitive fallback
            if (_paragonGlyphsByIdName.TryGetValue(id, out var glyph))
            {
                return glyph.Name;
            }
            // Fallback to case-insensitive search for edge cases
            glyph = _paragonGlyphs.FirstOrDefault(g => g.IdName.Equals(id, StringComparison.OrdinalIgnoreCase));
            return glyph?.Name ?? id;
        }

        public string GetParagonGlyphLocalisationByNumber(string id)
        {
            string number = id.Split('_').Last();
            // Try exact match first (fast path)
            if (_paragonGlyphsByIdName.TryGetValue(id, out var exactMatch))
            {
                return exactMatch.Name;
            }
            // Fallback to contains search for partial matches
            var glyph = _paragonGlyphs.FirstOrDefault(g => g.IdName.Contains(number, StringComparison.OrdinalIgnoreCase));
            return glyph?.Name ?? id;
        }

        public ItemAffix GetSigil(string affixId, string itemType)
        {
            var affixDefault = new ItemAffix
            {
                Id = affixId,
                Type = itemType,
                Color = _settingsManager.Settings.SelectedSigilDisplayMode.Equals("Whitelisting") ? Colors.Red : _settingsManager.Settings.DefaultColorNormal
            };

            if (!_affixPresetsByName.TryGetValue(_settingsManager.Settings.SelectedAffixPreset, out var preset))
                return affixDefault;

            var affix = preset.ItemSigils.FirstOrDefault(a => a.Id.Equals(affixId) && a.Type.Equals(itemType));
            if (affix == null) return affixDefault;
            return affix;
        }

        public string GetSigilDescription(string sigilId)
        {
            if (_sigilsByIdName.TryGetValue(sigilId, out var sigilInfo))
            {
                return sigilInfo.Description;
            }
            return string.Empty;
        }

        public string GetSigilDungeonTier(string sigilId)
        {
            return _sigilDungeonTiers.TryGetValue(sigilId, out var tier) ? tier : "F";
        }

        public string GetSigilType(string sigilId)
        {
            if (_sigilsByIdName.TryGetValue(sigilId, out var sigilInfo))
            {
                return sigilInfo.Type;
            }
            return string.Empty;
        }

        public string GetSigilName(string sigilId)
        {
            if (_sigilsByIdName.TryGetValue(sigilId, out var sigilInfo))
            {
                return sigilInfo.Name;
            }
            return string.Empty;
        }

        public ItemAffix GetUnique(string uniqueId, string itemType)
        {
            var affixDefault = new ItemAffix
            {
                Id = uniqueId,
                Type = itemType,
                Color = Colors.Red
            };

            if (!_affixPresetsByName.TryGetValue(_settingsManager.Settings.SelectedAffixPreset, out var preset))
                return affixDefault;

            var unique = preset.ItemUniques.FirstOrDefault(a => a.Id.Equals(uniqueId));
            if (unique == null) return affixDefault;
            return unique;
        }

        public string GetUniqueDescription(string uniqueId)
        {
            if (_uniquesByIdName.TryGetValue(uniqueId, out var uniqueInfo))
            {
                return uniqueInfo.Description;
            }
            return string.Empty;
        }

        public UniqueInfo? GetUniqueInfoMaxrollByIdSno(string idSno)
        {
            _uniquesByIdSno.TryGetValue(idSno, out var uniqueInfo);
            return uniqueInfo;
        }

        public string GetUniqueName(string uniqueId)
        {
            if (_uniquesByIdName.TryGetValue(uniqueId, out var uniqueInfo))
            {
                return uniqueInfo.Name;
            }
            return string.Empty;
        }

        public ItemAffix GetRune(string runeId, string itemType)
        {
            var affixDefault = new ItemAffix
            {
                Id = runeId,
                Type = itemType,
                Color = Colors.Red
            };

            if (!_affixPresetsByName.TryGetValue(_settingsManager.Settings.SelectedAffixPreset, out var preset))
                return affixDefault;

            var rune = preset.ItemRunes.FirstOrDefault(a => a.Id.Equals(runeId));
            if (rune == null) return affixDefault;
            return rune;
        }

        public string GetRuneDescription(string runeId)
        {
            if (_runesByIdName.TryGetValue(runeId, out var runeInfo))
            {
                return runeInfo.Description;
            }
            return string.Empty;
        }

        public string GetRuneName(string runeId)
        {
            if (_runesByIdName.TryGetValue(runeId, out var runeInfo))
            {
                return runeInfo.Name;
            }
            return string.Empty;
        }

        public string GetGearOrSigilAffixDescription(string affixId)
        {
            if (_affixesByIdName.TryGetValue(affixId, out var affixInfo))
                return affixInfo.Description;
            if (_sigilsByIdName.TryGetValue(affixId, out var sigilInfo))
                return sigilInfo.Name;
            if (_runesByIdName.TryGetValue(affixId, out var runeInfo))
                return runeInfo.Description;

            return string.Empty;
        }

        public bool IsDuplicate(ItemAffix itemAffix)
        {
            if (!_affixPresetsByName.TryGetValue(_settingsManager.Settings.SelectedAffixPreset, out var preset))
                return false;

            return preset.ItemAffixes.Count(affix =>
                affix.Type.Equals(itemAffix.Type) &&
                affix.Id.Equals(itemAffix.Id) &&
                affix.IsGreater.Equals(itemAffix.IsGreater) &&
                affix.IsTempered.Equals(itemAffix.IsTempered) &&
                affix.IsImplicit.Equals(itemAffix.IsImplicit)) > 1;
        }

        public void ResetMinimalAffixValues()
        {
            _minimalAffixValues.Clear();
            SaveAffixMinimalValueData();
        }

        public void SaveAffixColor(ItemAffix itemAffix)
        {
            SaveAffixPresets();

            WeakReferenceMessenger.Default.Send(new SelectedAffixesChangedMessage());
            WeakReferenceMessenger.Default.Send(new SelectedAspectsChangedMessage());
            WeakReferenceMessenger.Default.Send(new SelectedSigilsChangedMessage());
            WeakReferenceMessenger.Default.Send(new SelectedUniquesChangedMessage());
            WeakReferenceMessenger.Default.Send(new SelectedRunesChangedMessage());
        }

        private void LoadAffixPresets()
        {
            _affixPresets.Clear();
            _affixPresetsByName.Clear();

            string fileName = "Config/AffixPresets-v2.json";
            if (File.Exists(fileName))
            {
                using FileStream stream = File.OpenRead(fileName);
                _affixPresets = JsonSerializer.Deserialize<List<AffixPreset>>(stream) ?? new List<AffixPreset>();
            }

            // Sort list
            _affixPresets.Sort((x, y) =>
            {
                return string.Compare(x.Name, y.Name, StringComparison.Ordinal);
            });

            // Build lookup dictionary for O(1) access
            foreach (var preset in _affixPresets)
            {
                if (!string.IsNullOrEmpty(preset.Name))
                {
                    _affixPresetsByName[preset.Name] = preset;
                }
            }

            SaveAffixPresets();
        }

        private void ValidateAffixPresets()
        {
            foreach (AffixPreset preset in _affixPresets)
            {
                // Affixes
                foreach (var affix in preset.ItemAffixes)
                {
                    if (!_affixesByIdName.TryGetValue(affix.Id, out var affixInfo))
                    {
                        List<string> affixIds = affix.Id.Split(';').ToList();
                        int bestMatch = 0;
                        string newAffixId = string.Empty;

                        foreach (var affixInfoItem in _affixes)
                        {
                            int match = affixInfoItem.IdNameList.Where(a => affixIds.Contains(a)).Count();
                            if (match > bestMatch)
                            {
                                bestMatch = match;
                                newAffixId = affixInfoItem.IdName;
                            }
                        }

                        if (string.IsNullOrWhiteSpace(newAffixId))
                        {
                            WeakReferenceMessenger.Default.Send(new ErrorOccurredMessage(new ErrorOccurredMessageParams
                            {
                                Message = $"Build: \"{preset.Name}\": Affix not found. Replace missing affix or import build again."
                            }));
                        }
                        else
                        {
                            WeakReferenceMessenger.Default.Send(new ErrorOccurredMessage(new ErrorOccurredMessageParams
                            {
                                Message = $"Build: \"{preset.Name}\": Affix not found. Replaced \"{affix.Id}\"."
                            }));

                            affix.Id = newAffixId;
                        }
                    }
                }

                // Uniques
                foreach (var unique in preset.ItemUniques)
                {
                    if (!_uniquesByIdName.TryGetValue(unique.Id, out var uniqueInfo))
                    {
                        List<string> uniqueIds = unique.Id.Split(';').ToList();
                        int bestMatch = 0;
                        string newUniqueId = string.Empty;

                        foreach (var uniqueInfoItem in _uniques)
                        {
                            int match = uniqueInfoItem.IdNameList.Where(a => uniqueIds.Contains(a)).Count();
                            if (match > bestMatch)
                            {
                                bestMatch = match;
                                newUniqueId = uniqueInfoItem.IdName;
                            }
                        }

                        if (string.IsNullOrWhiteSpace(newUniqueId))
                        {
                            WeakReferenceMessenger.Default.Send(new ErrorOccurredMessage(new ErrorOccurredMessageParams
                            {
                                Message = $"Build: \"{preset.Name}\": Unique not found. Replace missing unique or import build again."
                            }));
                        }
                        else
                        {
                            WeakReferenceMessenger.Default.Send(new ErrorOccurredMessage(new ErrorOccurredMessageParams
                            {
                                Message = $"Build: \"{preset.Name}\": Unique not found. Replaced by \"{unique.Id}\"."
                            }));

                            unique.Id = newUniqueId;
                        }
                    }
                }
            }

            SaveAffixPresets();
        }

        public void RenamePreset(string oldName, string newName)
        {
            if (!_affixPresetsByName.TryGetValue(oldName, out var preset))
                return;

            // Update dictionary key
            _affixPresetsByName.Remove(oldName);
            preset.Name = newName;
            _affixPresetsByName[newName] = preset;

            SaveAffixPresets();
            _settingsManager.Settings.SelectedAffixPreset = newName;
            _settingsManager.SaveSettings();
        }

        public void SaveAffixPresets()
        {
            // Sort affixes
            foreach (var affixPreset in _affixPresets)
            {
                affixPreset.ItemAffixes.Sort((x, y) =>
                {
                    if (x.Id == y.Id && x.IsImplicit == y.IsImplicit && x.IsTempered == y.IsTempered) return 0;

                    int result = x.IsTempered && !y.IsTempered ? 1 : y.IsTempered && !x.IsTempered ? -1 : 0;
                    if (result == 0)
                    {
                        result = x.IsImplicit && !y.IsImplicit ? -1 : y.IsImplicit && !x.IsImplicit ? 1 : 0;
                    }
                    if (result == 0)
                    {
                        result = x.Id.CompareTo(y.Id);
                    }

                    return result;
                });
            }

            string fileName = "Config/AffixPresets-v2.json";
            string path = Path.GetDirectoryName(fileName) ?? string.Empty;
            Directory.CreateDirectory(path);

            using FileStream stream = File.Create(fileName);
            var options = new JsonSerializerOptions { WriteIndented = true };
            JsonSerializer.Serialize(stream, AffixPresets, options);
        }

        private void SaveAffixMinimalValueData()
        {
            string fileName = "./Config/MinimalAffixValues.json";
            string path = Path.GetDirectoryName(fileName) ?? string.Empty;
            Directory.CreateDirectory(path);

            using FileStream stream = File.Create(fileName);
            var options = new JsonSerializerOptions { WriteIndented = true };
            JsonSerializer.Serialize(stream, _minimalAffixValues, options);
        }

        private void SaveSigilDungeonTierData()
        {
            string fileName = "./Config/DungeonTiers.json";
            string path = Path.GetDirectoryName(fileName) ?? string.Empty;
            Directory.CreateDirectory(path);

            using FileStream stream = File.Create(fileName);
            var options = new JsonSerializerOptions { WriteIndented = true };
            JsonSerializer.Serialize(stream, _sigilDungeonTiers, options);
        }

        public void SetAffixMinimalValue(string idName, double minimalValue)
        {
            _minimalAffixValues[idName] = minimalValue;

            SaveAffixMinimalValueData();
        }

        public void SetSigilDungeonTier(SigilInfo sigilInfo, string tier)
        {
            _sigilDungeonTiers[sigilInfo.IdName] = tier;

            SaveSigilDungeonTierData();
        }

        public void SetIsAnyType(ItemAffix itemAffix, bool isAnyType)
        {
            if (!_affixPresetsByName.TryGetValue(_settingsManager.Settings.SelectedAffixPreset, out var preset))
                return;

            var affixes = preset.ItemAffixes.FindAll(a => a.Id.Equals(itemAffix.Id));
            foreach ( var affix in affixes )
            {
                affix.IsAnyType = isAnyType;
            }
        }

        private void ValidateMultiBuild()
        {
            foreach (MultiBuild multiBuild in _settingsManager.Settings.MultiBuildList)
            {
                if (!_affixPresetsByName.TryGetValue(multiBuild.Name, out _))
                {
                    WeakReferenceMessenger.Default.Send(new ErrorOccurredMessage(new ErrorOccurredMessageParams
                    {
                        Message = $"Multi build #{multiBuild.Index + 1} not found: {multiBuild.Name}."
                    }));
                }
            }
        }

        #endregion
    }
}
