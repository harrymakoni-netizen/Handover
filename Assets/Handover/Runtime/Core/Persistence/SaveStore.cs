using System;
using System.Collections.Generic;
using System.IO;

namespace Handover.Core
{
    /// <summary>The file operations the save store needs (substitutable in tests).</summary>
    public interface IFileSystem
    {
        bool Exists(string path);
        string ReadAllText(string path);
        void WriteAllText(string path, string text);
        void Delete(string path);
        void Move(string source, string destination);
        /// <summary>Replaces destination with source, keeping the old destination as backup.</summary>
        void Replace(string source, string destination, string backup);
        void EnsureDirectory(string path);
    }

    public sealed class PhysicalFileSystem : IFileSystem
    {
        public bool Exists(string path) => File.Exists(path);
        public string ReadAllText(string path) => File.ReadAllText(path);
        public void WriteAllText(string path, string text) => File.WriteAllText(path, text);
        public void Delete(string path) => File.Delete(path);
        public void Move(string source, string destination) => File.Move(source, destination);
        public void Replace(string source, string destination, string backup) => File.Replace(source, destination, backup);
        public void EnsureDirectory(string path) => Directory.CreateDirectory(path);
    }

    /// <summary>In-memory file system for deterministic tests and editor previews.</summary>
    public sealed class InMemoryFileSystem : IFileSystem
    {
        public readonly Dictionary<string, string> Files = new Dictionary<string, string>(StringComparer.Ordinal);
        /// <summary>When set, the next write throws after storing this many characters (simulated crash).</summary>
        public int? FailNextWriteAfterChars;

        public bool Exists(string path) => Files.ContainsKey(path);

        public string ReadAllText(string path)
        {
            if (!Files.TryGetValue(path, out string text))
            {
                throw new FileNotFoundException(path);
            }

            return text;
        }

        public void WriteAllText(string path, string text)
        {
            if (FailNextWriteAfterChars.HasValue)
            {
                int n = Math.Min(text.Length, FailNextWriteAfterChars.Value);
                FailNextWriteAfterChars = null;
                Files[path] = text.Substring(0, n);
                throw new IOException("Simulated interrupted write.");
            }

            Files[path] = text;
        }

        public void Delete(string path) => Files.Remove(path);

        public void Move(string source, string destination)
        {
            if (Files.ContainsKey(destination))
            {
                throw new IOException("Destination exists.");
            }

            Files[destination] = ReadAllText(source);
            Files.Remove(source);
        }

        public void Replace(string source, string destination, string backup)
        {
            string incoming = ReadAllText(source);
            Files[backup] = ReadAllText(destination);
            Files[destination] = incoming;
            Files.Remove(source);
        }

        public void EnsureDirectory(string path)
        {
        }
    }

    public sealed class SaveLoadResult
    {
        public SaveData Data;
        public bool FromBackup;
        public bool UsedDefaults;
        /// <summary>A recoverable message for the player, or null when nothing went wrong.</summary>
        public string Message;
    }

    /// <summary>
    /// Local save with atomic replacement: write a temporary file, verify it, replace the
    /// save only after success and keep the previous save as a backup. Loading falls back
    /// from the save to the backup to defaults, with a recoverable message.
    /// </summary>
    public sealed class SaveStore
    {
        public const string FileName = "handover-save.json";

        private readonly IFileSystem _fs;
        private readonly string _directory;
        private readonly Func<Checkpoint, List<string>> _validateCheckpoint;

        /// <param name="validateCheckpoint">Checkpoint rules for this chapter (rejects illegal capacities).</param>
        public SaveStore(IFileSystem fs, string directory, Func<Checkpoint, List<string>> validateCheckpoint)
        {
            _fs = fs;
            _directory = directory;
            _validateCheckpoint = validateCheckpoint;
        }

        public string SavePath => Path.Combine(_directory, FileName);
        public string TempPath => SavePath + ".tmp";
        public string BackupPath => SavePath + ".bak";

        public bool Save(SaveData data, out string error)
        {
            error = null;
            string json = SaveSerializer.ToJson(data);
            try
            {
                _fs.EnsureDirectory(_directory);
                _fs.WriteAllText(TempPath, json);

                // Verify what actually landed on disk before it replaces anything.
                if (!SaveSerializer.TryFromJson(_fs.ReadAllText(TempPath), out _, out string verifyError))
                {
                    error = "Save verification failed: " + verifyError;
                    TryDelete(TempPath);
                    return false;
                }

                if (_fs.Exists(SavePath))
                {
                    _fs.Replace(TempPath, SavePath, BackupPath);
                }
                else
                {
                    _fs.Move(TempPath, SavePath);
                }

                return true;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                error = "Could not write the save: " + e.Message;
                TryDelete(TempPath);
                return false;
            }
        }

        public SaveLoadResult Load()
        {
            bool mainExists = _fs.Exists(SavePath);
            bool backupExists = _fs.Exists(BackupPath);
            if (!mainExists && !backupExists)
            {
                return new SaveLoadResult { Data = new SaveData(), UsedDefaults = true };
            }

            if (mainExists && TryRead(SavePath, out SaveData data, out string checkpointNote))
            {
                return new SaveLoadResult { Data = data, Message = checkpointNote };
            }

            if (backupExists && TryRead(BackupPath, out data, out checkpointNote))
            {
                return new SaveLoadResult
                {
                    Data = data,
                    FromBackup = true,
                    Message = "Your latest save could not be read, so the previous one was restored.",
                };
            }

            return new SaveLoadResult
            {
                Data = new SaveData(),
                UsedDefaults = true,
                Message = "Saved progress could not be read and was reset. Settings are back to their defaults.",
            };
        }

        private bool TryRead(string path, out SaveData data, out string checkpointNote)
        {
            checkpointNote = null;
            data = null;
            string text;
            try
            {
                text = _fs.ReadAllText(path);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                return false;
            }

            if (!SaveSerializer.TryFromJson(text, out data, out _))
            {
                data = null;
                return false;
            }

            if (data.Checkpoint != null && _validateCheckpoint != null && _validateCheckpoint(data.Checkpoint).Count > 0)
            {
                // Settings and results survive; an illegal checkpoint is dropped with a message.
                data.Checkpoint = null;
                checkpointNote = "The saved rescue could not be resumed safely, so the chapter will start from the beginning.";
            }

            return true;
        }

        private void TryDelete(string path)
        {
            try
            {
                if (_fs.Exists(path))
                {
                    _fs.Delete(path);
                }
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                // Leave the stray temp file; it is never read.
            }
        }
    }
}
