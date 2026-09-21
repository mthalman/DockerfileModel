using Valleysoft.DockerfileModel.Tokens;

namespace Valleysoft.DockerfileModel.Tests;

/// <summary>Protects document write admission from cycles without preventing repairs of acyclic sharing.</summary>
public class StructuralEditingCycleTests
{
    /// <summary>Exercises every document write entry point against direct and indirect ancestor cycles.</summary>
    public static IEnumerable<object[]> CyclicWrites
    {
        get
        {
            string[] operations =
            {
                "add", "insert", "indexer", "replace", "replace-item", "remove", "remove-at",
                "clear", "clear-discard", "move", "move-self", "replace-self", "remove-missing",
                "replace-missing", "invalid-index", "interface-add", "interface-insert",
                "interface-indexer", "interface-remove", "interface-remove-at", "interface-clear"
            };
            foreach (string operation in operations)
            {
                yield return new object[] { operation, false };
                yield return new object[] { operation, true };
            }
        }
    }

    /// <summary>Existing cycles reject before serialization, selection, or mutation of either owner or newcomer.</summary>
    /// <param name="operation">The facade or standard-interface write being attempted.</param>
    /// <param name="indirect">Whether the cycle spans two ONBUILD objects instead of a direct self-reference.</param>
    [Theory]
    [MemberData(nameof(CyclicWrites))]
    public void DocumentWritesRejectCycles(string operation, bool indirect)
    {
        OnBuildInstruction cyclic = new(new RunInstruction("echo original"));
        OnBuildInstruction next = indirect ? new(new RunInstruction("echo nested")) : cyclic;
        cyclic.Instruction = next;
        next.Instruction = cyclic;
        Dockerfile document = new(new DockerfileConstruct[]
        {
            FromInstruction.Parse("FROM alpine\n"), cyclic
        });
        DockerfileConstruct[] items = document.Items.ToArray();
        Token[] rootTokens = cyclic.Tokens.ToArray();
        Token[] nextTokens = next.Tokens.ToArray();
        RunInstruction incoming = new("echo next");
        string incomingText = incoming.ToString();
        IList<DockerfileConstruct> compatibility = document.Items;

        Assert.Throws<InvalidOperationException>(() =>
        {
            switch (operation)
            {
                case "add": document.Items.Add(incoming); break;
                case "insert": document.Items.Insert(1, incoming); break;
                case "indexer": document.Items[1] = incoming; break;
                case "replace": document.Items.Replace(1, incoming, TriviaDisposition.Discard); break;
                case "replace-item": document.Items.ReplaceItem(cyclic, incoming); break;
                case "remove": document.Items.Remove(cyclic); break;
                case "remove-at": document.Items.RemoveAt(1); break;
                case "clear": document.Items.Clear(); break;
                case "clear-discard": document.Items.Clear(TriviaDisposition.Discard); break;
                case "move": document.Items.Move(1, 0); break;
                case "move-self": document.Items.Move(1, 1); break;
                case "replace-self": document.Items[1] = cyclic; break;
                case "remove-missing": document.Items.Remove(incoming); break;
                case "replace-missing": document.Items.ReplaceItem(incoming, new RunInstruction("echo other")); break;
                case "invalid-index": document.Items.Insert(-1, incoming); break;
                case "interface-add": compatibility.Add(incoming); break;
                case "interface-insert": compatibility.Insert(1, incoming); break;
                case "interface-indexer": compatibility[1] = incoming; break;
                case "interface-remove": compatibility.Remove(cyclic); break;
                case "interface-remove-at": compatibility.RemoveAt(1); break;
                case "interface-clear": compatibility.Clear(); break;
                default: throw new ArgumentOutOfRangeException(nameof(operation));
            }
        });

        Assert.Equal(items, document.Items);
        Assert.Equal(rootTokens, cyclic.Tokens);
        Assert.Equal(nextTokens, next.Tokens);
        Assert.Same(next, cyclic.Instruction);
        Assert.Same(cyclic, next.Instruction);
        Assert.Equal(incomingText, incoming.ToString());
    }

    /// <summary>Completed-node revisits remain admissible when a document edit removes acyclic sharing.</summary>
    /// <param name="sharedChild">Whether distinct instructions share a descendant rather than a top-level item.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RemovingSharedOccurrenceRepairsAcyclicDocument(bool sharedChild)
    {
        RunInstruction shared = new("echo shared");
        DockerfileConstruct first = shared;
        DockerfileConstruct second = shared;
        if (sharedChild)
        {
            OnBuildInstruction firstTrigger = new(new RunInstruction("echo first"));
            OnBuildInstruction secondTrigger = new(new RunInstruction("echo second"));
            firstTrigger.Instruction = shared;
            secondTrigger.Instruction = shared;
            first = firstTrigger;
            second = secondTrigger;
        }
        Dockerfile document = new(new[] { first, second });
        string survivor = first.ToString();
        Token[] survivorTokens = first.Tokens.ToArray();

        document.Items.RemoveAt(1, TriviaDisposition.Discard);

        Assert.Same(first, Assert.Single(document.Items));
        Assert.Equal(survivor, document.ToString());
        Assert.Equal(survivorTokens, first.Tokens);
        if (sharedChild)
        {
            Assert.Same(shared, Assert.IsType<OnBuildInstruction>(first).Instruction);
        }
    }

    /// <summary>Allowing acyclic sharing during admission does not allow publishing an unrepaired shared tree.</summary>
    [Fact]
    public void UnrepairedSharingStillRejectsPublication()
    {
        RunInstruction shared = new("echo shared");
        Dockerfile document = new(new DockerfileConstruct[] { shared, shared });
        string before = document.ToString();
        DockerfileConstruct[] items = document.Items.ToArray();

        Assert.Throws<InvalidOperationException>(() => document.Items.Add(new RunInstruction("echo next")));

        Assert.Equal(before, document.ToString());
        Assert.Equal(items, document.Items);
    }
}
