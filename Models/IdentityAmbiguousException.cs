using System;
using System.Collections.Generic;

namespace AdminInfoTools.Models
{
    public class IdentityAmbiguousException : Exception
    {
        public string AttemptedIdentity { get; }
        public List<string> Suggestions { get; }

        public IdentityAmbiguousException(string attemptedIdentity, List<string> suggestions)
            : base($"The identity '{attemptedIdentity}' was not found. Did you mean one of these: {string.Join(", ", suggestions)}?")
        {
            AttemptedIdentity = attemptedIdentity;
            Suggestions = suggestions;
        }
    }
}