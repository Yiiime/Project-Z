using System;
using System.IO;
using System.Text;

namespace ProjectZ.Core
{
    public interface IStateCodec
    {
        string Encode(WorldState state);
        WorldState Decode(string json);
    }

    public sealed class SaveStore
    {
        private readonly string path;
        private readonly string backup;
        private readonly IStateCodec codec;
        private readonly EpisodeCatalog catalog;
        public string Path => path;

        public SaveStore(string directory, IStateCodec codec, EpisodeCatalog catalog)
        {
            if (string.IsNullOrWhiteSpace(directory)) throw new ArgumentException("缺少存档目录。");
            this.codec = codec ?? throw new ArgumentNullException(nameof(codec));
            ContentRules.Validate(catalog);
            this.catalog = catalog;
            path = System.IO.Path.Combine(directory, "slot-1.json");
            backup = path + ".bak";
        }

        public bool Exists => File.Exists(path) || File.Exists(backup);

        public WorldState Load(out bool recovered)
        {
            recovered = false;
            if (TryLoad(path, out var state)) return state;
            if (TryLoad(backup, out state)) { recovered = true; return state; }
            throw new InvalidDataException("存档和备份都无法读取。原文件已保留，请另建新游戏或检查版本。");
        }

        public void Save(WorldState state)
        {
            GameSession.ValidateState(state, catalog);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
            var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                var bytes = Encoding.UTF8.GetBytes(codec.Encode(state));
                using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }
                if (!File.Exists(path)) File.Move(temp, path);
                else
                {
                    // Never replace a healthy backup with a corrupt primary file.
                    var validPrimary = TryLoad(path, out _);
                    File.Replace(temp, path, validPrimary ? backup : null);
                }
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }

        private bool TryLoad(string candidate, out WorldState state)
        {
            state = null;
            if (!File.Exists(candidate)) return false;
            try
            {
                var info = new FileInfo(candidate);
                if (info.Length > 4 * 1024 * 1024) return false;
                state = codec.Decode(File.ReadAllText(candidate));
                GameSession.ValidateState(state, catalog);
                return true;
            }
            catch (Exception exception) when (exception is ArgumentException || exception is InvalidDataException ||
                exception is IOException || exception is FormatException)
            { state = null; return false; }
        }
    }
}
