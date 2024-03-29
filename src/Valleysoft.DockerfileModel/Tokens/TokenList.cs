﻿using System.Collections;

namespace Valleysoft.DockerfileModel.Tokens;

public class TokenList<TToken> : IList<TToken>
    where TToken : Token
{
    private readonly IList<Token> innerTokens;
    private readonly Func<IEnumerable<TToken>, IEnumerable<TToken>>? filterTokens;

    internal TokenList(IList<Token> innerTokens, Func<IEnumerable<TToken>, IEnumerable<TToken>>? filterTokens = null)
    {
        this.innerTokens = innerTokens;
        this.filterTokens = filterTokens;
    }

    private IEnumerable<TToken> GetFilteredTokens()
    {
        IEnumerable<TToken> tokensOfType = innerTokens.OfType<TToken>();
        return filterTokens is null ? tokensOfType : filterTokens(tokensOfType);
    }

    public TToken this[int index]
    {
        get => GetFilteredTokens().ElementAt(index);
        set
        {
            index = innerTokens.IndexOf(this[index]);
            innerTokens[index] = value;
        }
    }

    public int Count => GetFilteredTokens().Count();

    public bool IsReadOnly => false;

    public void Add(TToken item) => ThrowAddRemoveNotSupported();

    public void Clear() => ThrowAddRemoveNotSupported();

    public bool Contains(TToken item) =>
        GetFilteredTokens().Contains(item);

    public void CopyTo(TToken[] array, int arrayIndex) =>
        GetFilteredTokens()
            .ToList()
            .CopyTo(array, arrayIndex);

    public IEnumerator<TToken> GetEnumerator() => GetFilteredTokens().GetEnumerator();

    public int IndexOf(TToken item) => GetFilteredTokens().ToList().IndexOf(item);

    public void Insert(int index, TToken item) => ThrowAddRemoveNotSupported();

    public bool Remove(TToken item)
    {
        ThrowAddRemoveNotSupported();
        return false;
    }

    public void RemoveAt(int index) => ThrowAddRemoveNotSupported();

    IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();

    private void ThrowAddRemoveNotSupported()
    {
        throw new NotSupportedException("Items may not be added or removed from the list.");
    }
}
