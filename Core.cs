using SRML;
using SRML.SR;
using SRML.SR.SaveSystem;
using SRML.SR.SaveSystem.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace FusionCore
{
    public static class Core
    {
        public static readonly Dictionary<string, Mode> fusionModes = new Dictionary<string, Mode>();
        public static readonly Dictionary<string, SlimeDefinition> pureSlimes = new Dictionary<string, SlimeDefinition>();
        public static readonly List<Identifiable.Id> exemptSlimes = new List<Identifiable.Id>
        {
            Identifiable.Id.GLITCH_TARR_SLIME,
            Identifiable.Id.TARR_SLIME
        };

        public static readonly CompoundDataPiece blames = new CompoundDataPiece("blames");
        public static void OnWorldDataLoad(CompoundDataPiece data)
        {
            string fixer(string id) => SaveHandler.data.enumTranslator.TranslateEnum(EnumTranslator.TranslationMode.FROMTRANSLATED, id);

            foreach (var p in data.DataList)
            {
                if (blames.DataList.Contains(p)) blames.DataList.Remove(p);
                if (p.data is string s) p.data = fixer(s);
                blames.DataList.Add(p);
            }
        }

        public static string AdjustCategoryName(string original)
        {
            if (blames.HasPiece(original))
                return "_" + blames.GetCompoundPiece(original).GetValue<string>("category");
            return original;
        }

        public static Identifiable.Id NewIdentifiableID(string name, SRMod mod = null)
        {
            if (Enum.IsDefined(typeof(Identifiable.Id), name)) throw new Exception($"{nameof(FusionCore)}: ID already exists: {name}");
            var id = InvokeAsStep(() => IdentifiableRegistry.CreateIdentifiableId(EnumPatcher.GetFirstFreeValue(typeof(Identifiable.Id)), name));
            if (mod is null) mod = SRMod.GetCurrentMod();
            IdentifiableRegistry.moddedIdentifiables[id] = mod;
            return id;
        }

        public static SlimeDefinition GetSlimeByFullName(string name)
        {
            SlimeDefinitions defns = SRSingleton<GameContext>.Instance.SlimeDefinitions;
            return defns.GetSlimeByIdentifiableId((Identifiable.Id)Enum.Parse(typeof(Identifiable.Id), name));
        }

        public static string GetFullName(this SlimeDefinition slime)
        {
            return slime.IdentifiableId.ToString();
        }

        public static void SetDisplayName(this SlimeDefinition slime, string name)
        {
            var entry = "l." + slime.GetFullName().ToLower();
            var hasDone = TranslationPatcher.doneDictionaries.ContainsKey("actor");
            if (hasDone && TranslationPatcher.doneDictionaries["actor"].ContainsKey(entry))
                TranslationPatcher.doneDictionaries["actor"].Remove(entry);
            InvokeAsStep(() => TranslationPatcher.AddActorTranslation(entry, name));
            if (hasDone) TranslationPatcher.doneDictionaries["actor"][entry] = name;
            if (TranslationPatcher.patches.ContainsKey("actor"))
                TranslationPatcher.patches["actor"][entry] = name;
        }

        public static string GetDisplayName(this SlimeDefinition slime, out Dictionary<string, string> dict)
        {
            var entry = "l." + slime.GetFullName().ToLower();
            dict = null;
            if (TranslationPatcher.doneDictionaries.ContainsKey("actor") && (dict = TranslationPatcher.doneDictionaries["actor"]).ContainsKey(entry))
            {
                return TranslationPatcher.doneDictionaries["actor"][entry];
            }
            if (TranslationPatcher.patches.ContainsKey("actor") && (dict = TranslationPatcher.patches["actor"]).ContainsKey(entry))
                return TranslationPatcher.patches["actor"][entry];
            return null;
        }

        public static string GetDisplayName(this SlimeDefinition slime)
        {
            return GetDisplayName(slime, out _);
        }

        public static bool ResolveMissingID(ref string value)
        {
            var data = blames.GetCompoundPiece(value);
            var blame = data.GetValue<string>("mode");
            if (!fusionModes.TryGetValue(blame, out var strat))
            {
                Log.Error($"{nameof(FusionCore)}: No strategy '{blame}' exists to fix missing ID {value}!");
                return false;
            }
            var components = strat.ParseComponents(data.GetValue<string[]>("components"));
            var parameters = strat.ParseParameters(data.GetValue<string[]>("parameters"));
            value = strat.Produce(components, parameters).GetFullName();
            return true;
        }

        public static string EncodeBlames(CompoundDataPiece blames)
        {

            var s = new List<string>();

            foreach (var data in blames.DataList.Select(e => (CompoundDataPiece)e))
            {
                s.Add(data.key);
                s.Add($"{data.GetValue("mode")}");
                s.Add($"{data.GetValue("category")}");
                s.Add($"{string.Join("\t", data.GetValue<string[]>("components"))}");
                s.Add($"{string.Join("\t", data.GetValue<string[]>("parameters"))}");
            }
            return string.Join("\n",s);
        }

        public static CompoundDataPiece DecodeBlames(string blamestring)
        {
            var blames = new CompoundDataPiece("blames");
            CompoundDataPiece data = null;
            int i = 0;
            foreach (var line in blamestring.Split('\n'))
            {
                var split = line == "" ? new string[] { } : line.Split('\t');
                switch (i++)
                {
                    case 0:
                        blames.AddPiece(data = new CompoundDataPiece(line)); break;
                    case 1:
                        data.SetValue("mode", line); break;
                    case 2:
                        data.SetValue("category", line); break;
                    case 3:
                        data.SetValue("components", split); break;
                    case 4:
                        data.SetValue("parameters", split); break;
                }
                i %= 5;
            }
            return blames;
        }

        public static string EncodeHash(int value)
        {
            //https://stackoverflow.com/a/33729594
            var chars = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
            var n = chars.Length;
            var result = new StringBuilder();
            value = Math.Abs(value);

            while (value != 0)
                { result.Append(chars[value % n]); value /= n; }

            return result.ToString();
        }

        public static void Setup()
        {
            foreach (var pure in AllSlimes().Where(slime => !slime.IsLargo))
            {
                pureSlimes.Add(PureName(pure.GetFullName()), pure);
            }
        }

        public static List<SlimeDefinition> AllSlimes()
        {
            SlimeDefinitions defns = SRSingleton<GameContext>.Instance.SlimeDefinitions;
            return defns.Slimes.Where(slime => !exemptSlimes.Contains(slime.IdentifiableId) && !Config.exclude.Split(' ').Contains(PureName(slime.GetFullName()))).ToList();
        }

        public static string PureName(string name)
        {
            name = Regex.Replace(name, @"((_SLIME)|(_LARGO)).*", "");
            return name;
        }

        public static List<string> Decompose(string body, IEnumerable<string> parts, out string rest)
        {
            rest = body;
            var found = new List<(int, string)>();
            foreach (var part in parts.OrderByDescending(p => p.Length))
            {
                var pos = rest.IndexOf(part);
                while (pos >= 0)
                {
                    found.Add((pos, part));
                    rest = rest.Remove(pos, part.Length);
                    pos = rest.IndexOf(part);
                }
            }
            return found.OrderBy(p => p.Item1).Select(p => p.Item2).ToList();
        }

        public static List<string> Decompose(string body, IEnumerable<string> parts)
        {
            return Decompose(body, parts, out _);
        }

        public static List<string> DecomposePureSlimeNames(string name)
        {
            return Decompose(PureName(name), pureSlimes.Keys);
        }

        public static List<string> DecomposePureSlimeFullNames(string name)
        {
            return DecomposePureSlimeNames(name).Select(n => pureSlimes[n]).Select(GetFullName).ToList();
        }

        public static List<string> PureSlimeFullNames(string name)
        {
            if (blames.HasPiece(name))
            {
                return blames.GetCompoundPiece(name).GetValue<string[]>("components").ToList();
            }
            return DecomposePureSlimeFullNames(name);
        }

        public static List<string> PureSlimeFullNames(IEnumerable<string> names)
        {
            return names.SelectMany(PureSlimeFullNames).ToList();
        }

        public static List<string> PureSlimeFullNames(params string[] names)
        {
            return PureSlimeFullNames(names);
        }

        public static bool PureNameExists(string name)
        {
            return pureSlimes.Keys.Contains(PureName(name));
        }

        public static string TitleCase(string s)
        {
            if (s == "") return s;
            if (s.Contains(' '))
            {
                return string.Join(" ", s.Split(' ').Select(TitleCase));
            }
            if (s.Contains('-'))
            {
                return string.Join("-", s.Split('-').Select(TitleCase));
            }
            return s.First().ToString().ToUpper() + s.Substring(1).ToLower();
        }

        public static string DisplayName(string suffix, List<SlimeDefinition> components)
        {
            return string.Join(" ", PureSlimeFullNames(components.Select(GetFullName))
                .Select(GetSlimeByFullName).Select(s => GetDisplayName(s) ?? TitleCase(s.GetFullName().Replace("_", " "))).Select(s => s.Contains(' ') ? s.Substring(0,s.LastIndexOf(' ')) : s).Append(suffix));
        }

        public static void InvokeAsStep(Action run, SRModLoader.LoadingStep step = SRModLoader.LoadingStep.PRELOAD)
        {
            Main.SRModLoader_get_CurrentLoadingStep._override = true;
            Main.SRModLoader_get_CurrentLoadingStep._step = step;
            run();
            Main.SRModLoader_get_CurrentLoadingStep._override = false;
        }

        public static T InvokeAsStep<T>(Func<T> run, SRModLoader.LoadingStep step = SRModLoader.LoadingStep.PRELOAD)
        {
            Main.SRModLoader_get_CurrentLoadingStep._override = true;
            Main.SRModLoader_get_CurrentLoadingStep._step = step;
            var value = run();
            Main.SRModLoader_get_CurrentLoadingStep._override = false;
            return value;
        }
    }
}