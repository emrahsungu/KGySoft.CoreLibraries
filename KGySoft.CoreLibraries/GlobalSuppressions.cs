// This file is used by Code Analysis to maintain SuppressMessage 
// attributes that are applied to this project.
// Project-level suppressions either have no target or are given 
// a specific target and scoped to a namespace, type, member, etc.
//
// To add a suppression to this file, right-click the message in the 
// Code Analysis results, point to "Suppress Message", and click 
// "In Suppression File".
// You do not need to add suppressions to this file manually.

using System.Diagnostics.CodeAnalysis;

// General
[assembly: SuppressMessage("Microsoft.Usage", "CA2243:AttributeStringLiteralsShouldParseCorrectly",
    Justification = "AssemblyInformationalVersion reflects the nuget versioning convention.")]
[assembly: SuppressMessage("Microsoft.Naming", "CA1709:IdentifiersShouldBeCasedCorrectly", MessageId = "Gy",
    Justification = "KGy stands for initials")]
[assembly: SuppressMessage("Microsoft.Naming", "CA1704:IdentifiersShouldBeSpelledCorrectly", MessageId = "Gy",
    Justification = "KGy stands for initials")]
[assembly: SuppressMessage("Style", "IDE0034:Simplify 'default' expression", Justification = "Should remain if helps understanding the code")]

// Static constructors (actually static field initializers)
[assembly: SuppressMessage("Microsoft.Design", "CA1065:DoNotRaiseExceptionsInUnexpectedLocations", Scope = "member", Target = "KGySoft.CoreLibraries.Enum`1.#.cctor()",
    Justification = "TEnum cannot be constrained otherwise correctly. In Release build issue is solved by RecompILer")]
[assembly: SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope", Scope = "member", Target = "KGySoft.ComponentModel.Command.#.cctor()",
    Justification = "Static command, will never be disposed.")]
[assembly: SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope", Scope = "member", Target = "KGySoft.Res.#.cctor()",
    Justification = "Static instance, will never be disposed.")]
[assembly: SuppressMessage("Microsoft.Maintainability", "CA1506:AvoidExcessiveClassCoupling", Scope = "member", Target = "KGySoft.Serialization.BinarySerializationFormatter.#.cctor()",
    Justification = "BinarySerializationFormatter supports many types natively, this is intended. See also its DataTypes enum.")]
