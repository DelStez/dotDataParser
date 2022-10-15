using SharpCompress.Archives.Zip;
using SharpCompress.Readers;
using System;
using System.Linq;
using System.IO;
using System.Text.RegularExpressions;
using CommandLine;
using System.Collections.Generic;
using System.Collections;

namespace r2_items_parser
{
    class Program
    {
        static readonly string R2_RFS_PASS = "4a3408a275b0343719ae2ab7250a8cab0c03b2178a58f2de";
        static readonly System.Text.Json.JsonSerializerOptions DefaultJsonSerializeOptions;

        static Program()
        {
            DefaultJsonSerializeOptions = new System.Text.Json.JsonSerializerOptions
            {
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                PropertyNamingPolicy= System.Text.Json.JsonNamingPolicy.CamelCase,
                WriteIndented = true
            };
        }

        /// <summary>
        /// Параметры командной строки
        /// </summary>
        public class CMDOptions
        {

            [Option('d', "datfile", Required = false, Default = @"Info.dat", HelpText = ".dat файл конфигурации предметов из etc/etc.rfs")]
            public string DatInfo { get; set; }

            [Option('g', "gamefolder", Required = false, Default = @".\", HelpText = "Путь к папке с игрой")]
            public string GameDir { get; set; }

            [Option('l', "langpack", Required = false, Default = "LangPaRU.tsv", HelpText = "tsv файл локализации из gui/gui.rfs который нужно использовать для сопоставления названий предметов")]
            public string LangPack { get; set; }

            [Option("psize", Required = false, Default = 56, HelpText = "Размер полезной нагрузки одного предмета в .dat файле")]
            public int PayloadSize { get; set; }
        }

        /// <summary>
        /// Прочитать файл из архива r2
        /// </summary>
        /// <param name="zipPath"></param>
        /// <param name="fileName"></param>
        /// <returns></returns>
        static byte[] ReadR2RfsFile(string zipPath, string fileName)
        {
            Console.WriteLine($"Чтение файла {fileName} из {zipPath}");
            using (var archive = ZipArchive.Open(zipPath, new ReaderOptions { LookForHeader = true, Password = R2_RFS_PASS }))
            {
                var datInfoEn = archive.Entries.FirstOrDefault((e) => e.Key == fileName);
                if (datInfoEn != null)
                {
                    using (var memStream = new MemoryStream())
                    using (var arcStream = datInfoEn.OpenEntryStream())
                    {
                        arcStream.CopyTo(memStream);
                        return memStream.GetBuffer();
                    }
                }
            }

            throw new FileNotFoundException($"Файл \"{Path.Join(zipPath, fileName)}\" не найден");
        }

        /// <summary>
        /// Получить список имен и описаний предметов из файла локализации
        /// </summary>
        /// <param name="langPaRuCsv"></param>
        /// <returns></returns>
        static Dictionary<string, Dictionary<string, object>> GetItemsFromLangfile(string langPaRuCsv)
        {
            Console.WriteLine($"Парсим названия предметов из файла локализации. Размер файла: {langPaRuCsv.Length}");
            var descriptions = new Dictionary<string, Dictionary<string, object>>();
            var rgx = new Regex(@"""\d""\t""(2|1)""\t""(\d+)""\t""""\t""(.+)""");
            var matches = rgx.Matches(langPaRuCsv);
            var counter = 0;
            foreach (Match match in matches)
            {
                var type = match.Groups[1].Value;
                var id = match.Groups[2].Value;
                var str = match.Groups[3].Value;

                if (!descriptions.ContainsKey(id))
                {
                    descriptions.Add(id, new Dictionary<string, object>());
                    counter++;
                }

                descriptions.TryGetValue(id, out Dictionary<string, object> item);

                if (type == "2")
                {
                    item["name"] = str;
                }
                else
                {
                    item["description"] = str;
                }
            }
            Console.WriteLine($"Спарсено названий предметов: {counter}");
            return descriptions;
        }

        /// <summary>
        /// Сериализовать оюъект в JSON
        /// </summary>
        /// <param name="dict"></param>
        /// <returns></returns>
        static string SerializeToJson(object dict)
        {
            return System.Text.Json.JsonSerializer.Serialize(dict, DefaultJsonSerializeOptions);
        }

        /// <summary>
        /// Проверка папки с игрой
        /// </summary>
        /// <param name="dir"></param>
        static void CheckGameDir(string dir)
        {
            if (!(File.Exists(Path.Combine(dir, @"etc\etc.rfs")) && File.Exists(Path.Combine(dir, @"gui\gui.rfs")))) {
                throw new FileNotFoundException($"Не найдены файлы игры в \"{dir}\". Запустите программу в папке с игрой или укажите путь к папке");
            }
        }


        /// <summary>
        /// Запустить парсер
        /// </summary>
        /// <param name="options"></param>
        static void RunParser(CMDOptions options)
        {
            CheckGameDir(options.GameDir);

            byte[] infoDat = ReadR2RfsFile(Path.Combine(options.GameDir, @"etc\etc.rfs"), options.DatInfo);
            byte[] langPackCsv = ReadR2RfsFile(Path.Combine(options.GameDir, @"gui\gui.rfs"), options.LangPack);

            var itemsCfg = GetItemsFromLangfile(System.Text.Encoding.Unicode.GetString(langPackCsv));

            var totalItems = BitConverter.ToInt32(infoDat);
            Console.WriteLine($"Кол-во предметов в Info.dat: {totalItems}");

            var offset = 4;
            var coutner = 0;

            for (var i = 0; i < totalItems; i++)
            {
                coutner++;
                // первым всегда идет id предмета
                var itemId = BitConverter.ToInt32(infoDat, offset);

                // сдвигаем на 4 байта после прочтения int32
                offset += 4;

                // потом идет название предмета (на корейском или еще каком-то не понятном языке)
                // перед названием, идут 4 байта отвечающие за длину названия
                // пропускаем название предмета + 4 байта
                offset += BitConverter.ToInt32(infoDat, offset) + 4;

                // дальне идет некая полезная для нас инфомрация
                var payload1 = new byte[14];
                Array.Copy(infoDat, offset, payload1, 0, 14);
                offset += payload1.Length;

                // потом идет описание предмета (на корейском или еще каком-то не понятном языке)
                // перед описанием идут 4 байта отвечающие за длину описания
                // пропускаем описание предмета + 4 байта
                offset += BitConverter.ToInt32(infoDat, offset) + 4;

                // далее снова идет некоторая полезная для нас информация
                var payload2 = new byte[14];
                Array.Copy(infoDat, offset, payload2, 0, 14);
                offset += payload2.Length;

                // пропускаем 2 подряд идущих значения, неизвестно за что отвечающих
                // по аналогии с названием и описанием (длина + само значение)
                var unknownLength = BitConverter.ToInt32(infoDat, offset);
                var unknownData = new byte[unknownLength];
                Array.Copy(infoDat, offset, unknownData, 0, unknownLength);
                offset += unknownLength + 4;

                var unknown2Length = BitConverter.ToInt32(infoDat, offset);
                var unknown2Data = new byte[unknown2Length];
                Array.Copy(infoDat, offset, unknown2Data, 0, unknown2Length);
                offset += unknown2Length + 4;

                // это последний, самый большой пак полезной инфомрации.
                var payload3 = new byte[options.PayloadSize];
                Array.Copy(infoDat, offset, payload3, 0, options.PayloadSize);
                offset += payload3.Length;

                Dictionary<string, object> item;
                var itemIdStr = itemId.ToString();

                if (itemsCfg.ContainsKey(itemIdStr))
                {
                    item = itemsCfg[itemIdStr];
                }
                else
                {
                    item = new Dictionary<string, object>();
                    itemsCfg.Add(itemIdStr, item);
                }

                item.Add("id", itemId);
                item.Add("type", BitConverter.ToInt32(payload1, 0));
                item.Add("stack", BitConverter.ToUInt16(payload1, 4)); //стакается ли предмет
                item.Add("weight", BitConverter.ToUInt16(payload1, 8));
                item.Add("time", BitConverter.ToInt32(payload1, 10)); // является ли предмет временным
                item.Add("useType", BitConverter.ToUInt16(payload2, 0)); // тип использования

                item.Add("attackDistance", BitConverter.ToUInt16(payload3, 0));
                item.Add("classes", payload3[2]);
                item.Add("dropModel", BitConverter.ToInt32(payload3, 3));
                item.Add("lvl", BitConverter.ToUInt16(payload3, 7));
                item.Add("locked", payload3[30]); // является ли предмет замочным по умолчанию
                item.Add("premium", payload3[31]); // является ли предет премиумным
                item.Add("category", payload3[32]); // категория предметов (как на ауке, оружие ближнего боя, дальнего боя, и тд)
                item.Add("runes", payload3[54]); // кол-во слотов под руны
                

#if DEBUG
                item.Add("unknownData", BitConverter.ToString(unknownData));
                item.Add("unknown2Data", BitConverter.ToString(unknown2Data));
                item.Add("payload1", BitConverter.ToString(payload1));
                item.Add("payload2", BitConverter.ToString(payload2));
                item.Add("payload3", BitConverter.ToString(payload3));
#endif
            }

            var itemEffectsCount = BitConverter.ToInt32(infoDat, offset);
            offset += 4;

            for (var i = 0; i < itemEffectsCount; i++)
            {
                var itemId = BitConverter.ToInt32(infoDat, offset);
                offset += 4;
                var effectId = BitConverter.ToInt32(infoDat, offset);
                offset += 4;


                if (itemsCfg.TryGetValue(itemId.ToString(), out Dictionary<string, object> item))
                {
                    if (item.ContainsKey("effects"))
                    {
                        var effecs = (List<int>)item["effects"];
                        effecs.Add(effectId);
                    } else
                    {
                        item["effects"] = new List<int>() { 
                            effectId
                        };
                    }
                    
                }
            }


            var mobsCount = BitConverter.ToInt32(infoDat, offset);
            offset += 4;
            Console.WriteLine($"Кол-во конфигов мобов: {mobsCount}");

            for (var i = 0; i < mobsCount; i++)
            {
                offset += 4; // id моба
                offset += 4 + BitConverter.ToInt32(infoDat, offset); // размер названия + название моба
                offset += 41;  // размер моба, тип (нпс/моб), скорости движения/атаки по умолчанию (или что-то другое), видна ли полоска хп и другие настройки
            }

            var itemIconsAndModelsCount = BitConverter.ToInt32(infoDat, offset);
            offset += 4;
            Console.WriteLine($"Кол-во конфигов иконок и моделей предметов: {itemIconsAndModelsCount}");

            for (var i = 0; i < itemIconsAndModelsCount; i++)
            {
                var n = BitConverter.ToInt32(infoDat, offset);
                offset += 4;
                var itemId = BitConverter.ToInt32(infoDat, offset);
                offset += 4;
                var type = BitConverter.ToInt32(infoDat, offset);
                offset += 4;

                var fNameLength = BitConverter.ToInt32(infoDat, offset);
                offset += 4;
                var nameBytes = new byte[fNameLength - 1];
                Array.Copy(infoDat, offset, nameBytes, 0, nameBytes.Length);
                var fName = System.Text.Encoding.ASCII.GetString(nameBytes);
                offset += fNameLength;

                var x = BitConverter.ToInt32(infoDat, offset);
                offset += 4;

                var y = BitConverter.ToInt32(infoDat, offset);
                offset += 4;

                if (itemsCfg.TryGetValue(itemId.ToString(), out Dictionary<string, object> item))
                {
                    if (!item.ContainsKey("icons"))
                    {
                        item["icons"] = new List<object>();
                    }
                    ((List<object>)item["icons"]).Add(new object[]
                    {
                        n,
                        type,
                        fName,
                        BitConverter.ToString(nameBytes),
                        x,
                        y
                    });
                }
            }

            Console.WriteLine($"Эффекты предметов ${BitConverter.ToInt32(infoDat, offset)}");

            var resultpath = Path.Combine(options.GameDir, "items.json");
            Console.WriteLine($"Парсинг завершен. Спарсено предметов: {itemsCfg.Count}");
            File.WriteAllText(resultpath, SerializeToJson(itemsCfg));
            Console.WriteLine($"Реультат записан в {resultpath}");
        }

        static void RunAttackDistanceHack(CMDOptions options)
        {
            byte[] infoDat = File.ReadAllBytes(Path.Combine(options.GameDir, @"etc\etc.rfs", options.DatInfo));

            var totalItems = BitConverter.ToInt32(infoDat);
            Console.WriteLine($"Кол-во предметов в Info.dat: {totalItems}");

            var offset = 4;
            var coutner = 0;

            for (var i = 0; i < totalItems; i++)
            {
                coutner++;
                // первым всегда идет id предмета
                var itemId = BitConverter.ToInt32(infoDat, offset);

                // сдвигаем на 4 байта после прочтения int32
                offset += 4;

                // потом идет название предмета (на корейском или еще каком-то не понятном языке)
                // перед названием, идут 4 байта отвечающие за длину названия
                // пропускаем название предмета + 4 байта
                offset += BitConverter.ToInt32(infoDat, offset) + 4;

                // дальне идет некая полезная для нас инфомрация
                var payload1 = new byte[14];
                Array.Copy(infoDat, offset, payload1, 0, 14);
                offset += payload1.Length;

                // потом идет описание предмета (на корейском или еще каком-то не понятном языке)
                // перед описанием идут 4 байта отвечающие за длину описания
                // пропускаем описание предмета + 4 байта
                offset += BitConverter.ToInt32(infoDat, offset) + 4;

                // далее снова идет некоторая полезная для нас информация
                var payload2 = new byte[14];

                offset += payload2.Length;

                // пропускаем 2 подряд идущих значения, неизвестно за что отвечающих
                // по аналогии с названием и описанием (длина + само значение)
                var unknownLength = BitConverter.ToInt32(infoDat, offset);
                var unknownData = new byte[unknownLength];
                Array.Copy(infoDat, offset, unknownData, 0, unknownLength);
                offset += unknownLength + 4;

                var unknown2Length = BitConverter.ToInt32(infoDat, offset);
                var unknown2Data = new byte[unknown2Length];
                Array.Copy(infoDat, offset, unknown2Data, 0, unknown2Length);
                offset += unknown2Length + 4;

                // это последний, самый большой пак полезной инфомрации.
                var payload3 = new byte[options.PayloadSize];
                var payload3Offset = offset;
                Array.Copy(infoDat, offset, payload3, 0, options.PayloadSize);
                offset += payload3.Length;

                // подмена дальности атаки у оружий
                if ((payload3[5] == 0 && payload3[32] == 0) || (payload3[5] == 0 && payload3[32] == 1))
                {
                    var distanceOffset = payload3Offset;
                    foreach (var b in BitConverter.GetBytes((UInt16)3200))
                    {
                        infoDat[distanceOffset++] = b;
                    }
                }
            }
            var newInfoDatPath = Path.Combine(Path.Combine(options.GameDir, @"etc", options.DatInfo));
            File.WriteAllBytes(newInfoDatPath, infoDat);
            Console.WriteLine($"Изменение дальности атаки в Info.dat записано в {newInfoDatPath}");
        }

        static void Main(string[] args)
        {
            Parser.Default.ParseArguments<CMDOptions>(args)
               .WithParsed(o => {
                   if (args.Length > 0)
                   {
                       if (args[0] == "hack")
                       {
                           RunAttackDistanceHack(o);
                       }
                       else
                       {
                           RunParser(o);
                       }
                   }
                });
        }
    }
}
