using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Remoting.Metadata.W3cXsd2001;

namespace SchemeTool
{
    class Program
    {
        static void Main(string[] args)
        {
            string gameTitle = "TenShiSouZou";
            string exeName = "tenshi_sz.exe";

            string formatPath = @".\GameData\Formats.dat";
            string controlBlockFile = "LLLJ.bin";
            string namesFile = "HxNames-Tenshi.lst";

            // 1. Load existing Formats.dat
            using (Stream stream = File.OpenRead(formatPath))
            {
                GameRes.FormatCatalog.Instance.DeserializeScheme(stream);
            }

            // 2. Find XP3 opener
            var format = GameRes.FormatCatalog.Instance.ArcFormats
                .FirstOrDefault(a => a is GameRes.Formats.KiriKiri.Xp3Opener)
                as GameRes.Formats.KiriKiri.Xp3Opener;

            if (format == null)
            {
                Console.WriteLine("Xp3Opener not found.");
                return;
            }

            var scheme = format.Scheme as GameRes.Formats.KiriKiri.Xp3Scheme;
            if (scheme == null)
            {
                Console.WriteLine("Xp3Scheme not found.");
                return;
            }

            // 3. Load ControlBlock
            if (!File.Exists(controlBlockFile))
            {
                Console.WriteLine("LLLJ.bin not found.");
                return;
            }

            byte[] cb = File.ReadAllBytes(controlBlockFile);

            if (cb.Length % 4 != 0)
            {
                Console.WriteLine("ControlBlock size invalid.");
                return;
            }

            uint[] controlBlock = new uint[cb.Length / 4];
            Buffer.BlockCopy(cb, 0, controlBlock, 0, cb.Length);

            // 4. Build CxScheme using Frida parameters
            var cs = new GameRes.Formats.KiriKiri.CxScheme
            {
                Mask = 0x1dc,
                Offset = 0x295,
                PrologOrder = new byte[] { 0, 2, 1 },
                OddBranchOrder = new byte[] { 4, 1, 0, 3, 5, 2 },
                EvenBranchOrder = new byte[] { 3, 4, 1, 2, 6, 0, 7, 5 },
                ControlBlock = controlBlock
            };

            // 5. Build HxCrypt
            var crypt = new GameRes.Formats.KiriKiri.HxCrypt(cs)
            {
                RandomType = 1,
                FilterKey = 0xb9ac287fb5e05f0d,
                NamesFile = namesFile
            };

            // 6. Set IndexKey (Frida captured)
            byte[] key = SoapHexBinary.Parse(
                "724dc481e71024c53ed9ee2a1674f39caec40e0d55ffb11e60fe6e1f7dcc4128"
            ).Value;

            byte[] nonce = SoapHexBinary.Parse(
                "522f20d0a8e442e85a1e8b4dead28da3"
            ).Value;

            crypt.IndexKeyDict = new Dictionary<string, GameRes.Formats.KiriKiri.HxIndexKey>()
            {
                {
                    "data.xp3",
                    new GameRes.Formats.KiriKiri.HxIndexKey
                    {
                        Key1 = key,
                        Key2 = nonce
                    }
                }
            };

            // 7. Register scheme
            if (!scheme.KnownSchemes.ContainsKey(gameTitle))
                scheme.KnownSchemes.Add(gameTitle, crypt);
            else
                scheme.KnownSchemes[gameTitle] = crypt;

            // 8. Bind exe to scheme
            var gameMapField = typeof(GameRes.FormatCatalog)
                .GetField("m_game_map", BindingFlags.Instance | BindingFlags.NonPublic);

            var gameMap = gameMapField.GetValue(GameRes.FormatCatalog.Instance)
                as Dictionary<string, string>;

            if (gameMap != null)
                gameMap[exeName] = gameTitle;

            // 9. Save back to Formats.dat
            using (Stream stream = File.Create(formatPath))
            {
                GameRes.FormatCatalog.Instance.SerializeScheme(stream);
            }

            Console.WriteLine("TenShiSouZou scheme successfully added.");
        }
    }
}
