using System;

namespace LENet
{
    public sealed class ENetListNode<T> where T : ENetListNode<T>.Element
    {
        public abstract class Element
        {
            public readonly ENetListNode<T> Node;
            public Element() => Node = new ENetListNode<T>(this as T);
        }

        public abstract class Root
        {
            private readonly ENetListNode<T> _root = new ENetListNode<T>();

            public bool Empty => Begin == End;

            public ENetListNode<T> Begin => _root.Next;

            public ENetListNode<T> End => _root;

            public void Clear()
            {
                _root.Next = _root;
                _root.Prev = _root;
            }
        }

        public ENetListNode<T> Next { get; private set; }

        public ENetListNode<T> Prev { get; private set; }

        public readonly T Value;

        private ENetListNode()
        {
            Next = this;
            Prev = this;
            Value = default;
        }

        private ENetListNode(T v)
        {
            Next = null;
            Prev = null;
            Value = v;
        }

        // Inserts node before this node
        public ENetListNode<T> Insert(ENetListNode<T> what)
        {
            var where = this;

            what.Prev = Prev;
            what.Next = where;

            where.Prev.Next = what;
            where.Prev = what;

            return what;
        }

        // Removes this node
        public ENetListNode<T> Remove()
        {
            var what = this;

            what.Prev.Next = Next;
            what.Next.Prev = Prev;

            return what;
        }

        // Inserts range of nodes before this node
        public ENetListNode<T> Move(ENetListNode<T> first, ENetListNode<T> last)
        {
            var where = this;

            first.Prev.Next = last.Next;
            last.Next.Prev = first.Prev;

            first.Prev = Prev;
            last.Next = where;

            first.Prev.Next = first;
            where.Prev = last;

            return first;
        }
    }

    public sealed class ENetList<T> : ENetListNode<T>.Root where T : ENetListNode<T>.Element 
    { 

    }
}
