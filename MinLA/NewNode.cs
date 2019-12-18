using System.Collections.Generic;
using System.IO;

namespace MinLA
{
    public class NewNode
    {
        public static void WriteD3JsonFormat(NewNode[] nodes, string filename)
        {
            using var file = new StreamWriter(File.OpenWrite(filename));
            file.Write(@"
{
    ""nodes"":
    [ ");

            var useComma = false;
            var index = 0;
            for (var i = 0; i < nodes.Length - 1; i++)
            {
                if (nodes[i].Removed)
                {
                    continue;
                }

                nodes[i].NewId = index++;
                if (useComma)
                {
                    file.Write(",");
                }
                else
                {
                    useComma = true;
                }
                file.Write(
                    @"
        {
            ""x"":"+index+@",
            ""y"":"+index+@"
        }");
            }

            file.Write(
                @"
    ],
    ""links"":[  ");

            useComma = false;
            foreach (var node in nodes)
            {
                if (node.Removed)
                {
                    continue;
                }

                var id = node.NewId;
                foreach (var child in node.Children)
                {
                    var target = child.NewId;
                    if (useComma)
                    {
                        file.Write(",");
                    }
                    else
                    {
                        useComma = true;
                    }

                    file.Write(
@"
        {
            ""source"":"+id+@",
            ""target"":"+target+@"
        }");
                }
            }

            file.Write(
                @"
    ]
}");
        }

        public NewNode(int id)
        {
            _id = id;
        }

        public readonly HashSet<NewNode> Children = new HashSet<NewNode>();
        public readonly HashSet<NewNode> Parents = new HashSet<NewNode>();

        private readonly int _id;
        public int NewId;
        public override int GetHashCode()
        {
            return _id;
        }

        //public bool Terminal;

        public bool Removed;

        public void MergeChild(NewNode child)
        {
            Children.Remove(child);
            foreach (var grandchild in child.Children)
            {
                grandchild.Parents.Remove(child);
                if (Children.Add(grandchild))
                {
                    grandchild.Parents.Add(this);
                }
            }

            child.Parents.Remove(this);
            if (child.Parents.Count == 0)
            {
                child.Removed = true;
                child.Children.Clear();
            }
        }

        public override bool Equals(object? obj)
        {
            if (obj == null)
            {
                return false;
            }

            if (!(obj is NewNode other))
            {
                return false;
            }

            return _id == other._id;
        }
    }
}