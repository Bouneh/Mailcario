using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace Mailcario;

/// <summary>Replaces a grouped WPF list with one reset instead of intermediate move events.</summary>
internal sealed class ResettableObservableCollection<T> : ObservableCollection<T>
{
    internal bool ReplaceWith(IReadOnlyList<T> items)
    {
        if (Count == items.Count)
        {
            bool unchanged = true;
            for (int index = 0; index < items.Count; index++)
                if (!ReferenceEquals(this[index], items[index])) { unchanged = false; break; }
            if (unchanged) return false;
        }
        CheckReentrancy();
        Items.Clear();
        foreach (T item in items) Items.Add(item);
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        return true;
    }
}
