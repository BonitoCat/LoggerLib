using System;

namespace FileLib
{
    [Flags]
    public enum UnixFileModes : uint
    {
        UserRead    = 0x100,
        UserWrite   = 0x80,
        UserExecute = 0x40,

        GroupRead   = 0x20,
        GroupWrite  = 0x10,
        GroupExecute= 0x8,

        OtherRead   = 0x4,
        OtherWrite  = 0x2,
        OtherExecute= 0x1,

        UserRW  = UserRead | UserWrite,
        UserRWX = UserRead | UserWrite | UserExecute,
        GroupRX = GroupRead | GroupExecute,
        OtherRX = OtherRead | OtherExecute,
    }
}