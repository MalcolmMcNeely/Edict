# Stable command wire identity via generator-emitted `[Alias]` and `partial` commands

**Status:** accepted

Orleans' type manifest uses the full CLR type name (`Namespace.ClassName`) as the wire identity when polymorphically deserializing the abstract `Command` parameter on `IEdictSender.Send`. Renaming a command's namespace — without any stable alias — silently changes the type manifest key, breaking cross-silo deserialization of in-flight messages with no compile-time warning.

The fix is `[Orleans.AliasAttribute]`. Rather than requiring consumers to remember to write it, the `EdictCommandGenerator` auto-emits it as a second `partial record` declaration for every concrete command it discovers. The alias value is the simple class name (`nameof(TheClass)`), decoupling the wire identity from the CLR namespace.

Because a generator can only emit a `partial` declaration for a type the consumer has also declared `partial`, concrete commands must be `partial`. A new analyzer (EDICT006) errors if a non-abstract `Command` subtype is not `partial`, mirroring the EDICT001 rule that grains must be `partial` so the generator can emit their Orleans interface.

## Considered Options

- **Consumer writes `[Alias]` manually + analyzer enforces** — rejected: ceremony on every command definition; consumer must know the framework-internal reason; alias value is always `nameof(Class)` so there is nothing for the consumer to decide.
- **Programmatic alias registration via Orleans `ISerializerBuilder`** — rejected: would require `AddEdictContractSerializer` (or a new companion method) to accept a generated type list; couples the serializer registration to the generator output in a non-obvious way; no `partial` requirement but at the cost of an invisible, hard-to-audit registration path.
- **Full qualified name as alias** — rejected: defeats the purpose; namespace rename still breaks the alias.
- **Simple class name as alias (chosen)** — accepted: namespace-rename-safe; collision requires two commands with identical class names in the same Orleans cluster, which is a design smell the framework need not prevent.

## Consequences

- Every concrete `Command` subclass must be declared `partial`. EDICT006 errors otherwise.
- The generator emits `[global::Orleans.AliasAttribute("ClassName")] partial record ClassName;` in the command's own namespace, one source file per command.
- `CommandWireShapeTests` snapshots continue to guard property-name drift; the `ReceivedType` line in `CommandMessagePackRoundTrip` snapshots pins the alias-stable round-trip type name.
- Two commands with the same simple class name in the same cluster will collide on the alias. This is out of scope; the rule is: command class names must be unique within a deployed Orleans cluster.
- Events are out of scope — they do not exist in Edict yet.
