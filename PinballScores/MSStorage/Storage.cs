#nullable disable
using System.Runtime.InteropServices.ComTypes;

namespace PinballScores.MSStorage
{
    public class Storage
    {
        private IStorage storage;

        public Storage(string filename)
        {
            StorageImports.StgOpenStorage(filename, null, STGM.DIRECT | STGM.READ | STGM.SHARE_EXCLUSIVE, IntPtr.Zero, 0, out storage);
        }

        public IEnumerable<string> GetTableNames()
        {
            return GetKeys(storage);
        }

        public IEnumerable<KeyValuePair<string, string>> GetTableVariables(string table)
        {
            var sub = OpenSubStorage(storage, table);
            var keys = GetKeys(sub);
            return keys.Select(k => new KeyValuePair<string, string>(k, ReadValue(sub, k)));
        }

        public string ReadValue(string table, string key)
        {
            var tableStorage = OpenSubStorage(storage, table);
            var value = ReadStorageStream(tableStorage, key);
            return value;
        }
        public string ReadValue(IStorage tableStorage, string key)
        {
            var value = ReadStorageStream(tableStorage, key);
            return value;
        }

        private IStorage OpenSubStorage(IStorage root, string key)
        {
            try
            {
                root.OpenStorage(key, null, (uint)(STGM.DIRECT | STGM.READ | STGM.SHARE_EXCLUSIVE), IntPtr.Zero, 0, out IStorage sub);
                return sub;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                return null;
            }
        }

        private string ReadStorageStream(IStorage storage, string key)
        {
            try
            {
                storage.OpenStream(key, IntPtr.Zero, (uint)(STGM.DIRECT | STGM.READ | STGM.SHARE_EXCLUSIVE), 0, out IStream stream);
                stream.Stat(out STATSTG stat, (int)STATFLAG.STATFLAG_NONAME);
                var buffer = new byte[stat.cbSize];
                stream.Read(buffer, (int)stat.cbSize, IntPtr.Zero);
                return System.Text.Encoding.Unicode.GetString(buffer);
            }
            catch
            {
                return null;
            }
        }

        public IEnumerable<string> GetKeys(IStorage storage)
        {
            List<string> keys = new List<string>();

            System.Runtime.InteropServices.ComTypes.STATSTG statstg;
            storage.Stat(out statstg, (uint)STATFLAG.STATFLAG_DEFAULT);

            IEnumSTATSTG pIEnumStatStg = null;
            storage.EnumElements(0, IntPtr.Zero, 0, out pIEnumStatStg);

            System.Runtime.InteropServices.ComTypes.STATSTG[] regelt = { statstg };
            uint fetched = 0;
            uint res = pIEnumStatStg.Next(1, regelt, out fetched);

            if (res == 0)
            {
                while (res != 1)
                {
                    keys.Add(regelt[0].pwcsName);

                    if ((res = pIEnumStatStg.Next(1, regelt, out fetched)) != 1)
                    {
                        statstg = regelt[0];
                    }
                }
            }

            return keys;
        }
    }
}
