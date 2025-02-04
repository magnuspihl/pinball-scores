using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace PinballScores.MSStorage
{
    [ComImport]
    [Guid("0000000b-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IStorage
    {
        void CreateStream(
            /* [string][in] */ string pwcsName,
            /* [in] */ uint grfMode,
            /* [in] */ uint reserved1,
            /* [in] */ uint reserved2,
            /* [out] */ out IStream ppstm);

        void OpenStream(
            /* [string][in] */ string pwcsName,
            /* [unique][in] */ IntPtr reserved1,
            /* [in] */ uint grfMode,
            /* [in] */ uint reserved2,
            /* [out] */ out IStream ppstm);

        void CreateStorage(
            /* [string][in] */ string pwcsName,
            /* [in] */ uint grfMode,
            /* [in] */ uint reserved1,
            /* [in] */ uint reserved2,
            /* [out] */ out IStorage ppstg);

        void OpenStorage(
            /* [string][unique][in] */ string pwcsName,
            /* [unique][in] */ IStorage pstgPriority,
            /* [in] */ uint grfMode,
            /* [unique][in] */ IntPtr snbExclude,
            /* [in] */ uint reserved,
            /* [out] */ out IStorage ppstg);

        void CopyTo(
            /* [in] */ uint ciidExclude,
            /* [size_is][unique][in] */ Guid rgiidExclude, // should this be an array?
            /* [unique][in] */ IntPtr snbExclude,
            /* [unique][in] */ IStorage pstgDest);

        void MoveElementTo(
            /* [string][in] */ string pwcsName,
            /* [unique][in] */ IStorage pstgDest,
            /* [string][in] */ string pwcsNewName,
            /* [in] */ uint grfFlags);

        void Commit(
            /* [in] */ uint grfCommitFlags);

        void Revert();

        void EnumElements(
            /* [in] */ uint reserved1,
            /* [size_is][unique][in] */ IntPtr reserved2,
            /* [in] */ uint reserved3,
            /* [out] */ out IEnumSTATSTG ppenum);

        void DestroyElement(
            /* [string][in] */ string pwcsName);

        void RenameElement(
            /* [string][in] */ string pwcsOldName,
            /* [string][in] */ string pwcsNewName);

        void SetElementTimes(
            /* [string][unique][in] */ string pwcsName,
            /* [unique][in] */ System.Runtime.InteropServices.ComTypes.FILETIME pctime,
            /* [unique][in] */ System.Runtime.InteropServices.ComTypes.FILETIME patime,
            /* [unique][in] */ System.Runtime.InteropServices.ComTypes.FILETIME pmtime);

        void SetClass(
            /* [in] */ Guid clsid);

        void SetStateBits(
            /* [in] */ uint grfStateBits,
            /* [in] */ uint grfMask);

        void Stat(
            /* [out] */ out System.Runtime.InteropServices.ComTypes.STATSTG pstatstg,
            /* [in] */ uint grfStatFlag);

    }
}
