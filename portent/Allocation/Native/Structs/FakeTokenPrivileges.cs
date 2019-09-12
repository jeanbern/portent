namespace portent
{
    internal unsafe struct FakeTokenPrivileges
    {
        public uint PrivilegeCount;
        public fixed byte FakePrivileges[360];

        public FakeTokenPrivileges(uint _1)
        {
            PrivilegeCount = 30;
        }
    }
}
