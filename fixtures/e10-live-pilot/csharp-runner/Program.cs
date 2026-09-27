using System;
using System.Globalization;

if (args.Length != 2 || !long.TryParse(args[0], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long available) || !long.TryParse(args[1], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long quantity))
    return 64;

CandidateResult result = Candidate.Execute(available, quantity);
Console.WriteLine($"{result.Accepted}|{result.Available.ToString(CultureInfo.InvariantCulture)}|{result.Reserved.ToString(CultureInfo.InvariantCulture)}");
return 0;

public readonly record struct CandidateResult(bool Accepted, long Available, long Reserved);
