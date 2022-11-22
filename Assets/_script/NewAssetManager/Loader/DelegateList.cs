using System;
using System.Collections.Generic;

namespace FunPlus.AssetManagement
{
    public class DelegateList<T>
    {
        LinkedList<Action<T>> m_callbacks;
        bool m_invoking = false;
        
        private static Queue<LinkedListNode<Action<T>>> gcPool = new Queue<LinkedListNode<Action<T>>>();

        public int Count
        {
            get { return m_callbacks == null ? 0 : m_callbacks.Count; }
        }

        public void Add(Action<T> action)
        {
            var node = GetNode(action);
            if (m_callbacks == null)
                m_callbacks = new LinkedList<Action<T>>();
            m_callbacks.AddLast(node);
        }

        public void Remove(Action<T> action)
        {
            if (m_callbacks == null)
                return;

            var node = m_callbacks.First;
            while (node != null)
            {
                if (node.Value == action)
                {
                    if (m_invoking)
                    {
                        node.Value = null;
                    }
                    else
                    {
                        m_callbacks.Remove(node);
                        ReleaseNode(node);
                    }

                    return;
                }

                node = node.Next;
            }
        }

        public void Invoke(T res)
        {
            if (m_callbacks == null)
                return;

            m_invoking = true;
            var node = m_callbacks.First;
            while (node != null)
            {
                if (node.Value != null)
                {
                    try
                    {
                        node.Value(res);
                    }
                    catch (Exception ex)
                    {
                        UnityEngine.Debug.LogException(ex);
                    }
                }

                node = node.Next;
            }

            m_invoking = false;
            var r = m_callbacks.First;
            while (r != null)
            {
                var next = r.Next;
                if (r.Value == null)
                {
                    m_callbacks.Remove(r);
                    ReleaseNode(r);
                }

                r = next;
            }
        }

        public void Clear()
        {
            if (m_callbacks == null)
                return;
            var node = m_callbacks.First;
            while (node != null)
            {
                var next = node.Next;
                m_callbacks.Remove(node);
                ReleaseNode(node);
                node = next;
            }
        }

        private LinkedListNode<Action<T>> GetNode(Action<T> action)
        {
            if (gcPool.Count > 0)
            {
                var node = gcPool.Dequeue();
                node.Value = action;
            }
            return new LinkedListNode<Action<T>>(action);
        }

        private void ReleaseNode(LinkedListNode<Action<T>> node)
        {
            node.Value = null;
            gcPool.Enqueue(node);
        }
    }
}