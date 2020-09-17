namespace DockerfileModel
{
    internal abstract class TokenVisitor
    {
        public virtual void Visit(Token token)
        {
            if (token is AggregateToken aggregate)
            {
                VisitAggregateToken(aggregate);
            }
            else if (token is LiteralToken literal)
            {
                VisitLiteralToken(literal);
            }
        }

        protected virtual void VisitAggregateToken(AggregateToken token)
        {
            foreach (Token child in token.Tokens)
            {
                Visit(child);
            }
        }

        protected virtual void VisitLiteralToken(LiteralToken token) { }
    }
}
