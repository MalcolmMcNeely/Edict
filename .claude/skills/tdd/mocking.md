# When to Mock

Mock at **system boundaries** only:

- External APIs (payment, email, Azure Blob Storage, etc.)
- Databases (sometimes — prefer a real test DB with an in-memory provider or test containers)
- Time / randomness (`IClock`, `IDateTimeProvider`)
- File system (sometimes)

Don't mock:

- Your own domain classes / services
- Internal collaborators
- Anything you control

Use **FakeItEasy** for test doubles. Prefer **fakes** (hand-written or `A.Fake<T>()`) over mocks — fakes express intent and avoid coupling tests to call-count expectations.

## Designing for Mockability

At system boundaries, design interfaces that are easy to mock:

**1. Use dependency injection**

Inject external dependencies via the constructor — never instantiate infrastructure inside domain logic:

```csharp
// Easy to mock
public class PaymentService(IPaymentGateway gateway)
{
    public Task<PaymentResult> ProcessAsync(Order order) =>
        gateway.ChargeAsync(order.Total);
}

// Hard to mock — infrastructure leak
public class PaymentService
{
    public Task<PaymentResult> ProcessAsync(Order order)
    {
        var client = new StripeClient(Environment.GetEnvironmentVariable("STRIPE_KEY"));
        return client.ChargeAsync(order.Total);
    }
}
```

**2. Prefer narrow, operation-specific interfaces**

Create an interface method per external operation instead of one generic pass-through — each mock then returns exactly one specific shape:

```csharp
// GOOD: Each method is independently mockable
public interface IContractStorageClient
{
    Task<Stream> DownloadAsync(BlobReference reference);
    Task<BlobReference> UploadAsync(Guid tenantId, Stream content, string fileName);
    Task DeleteAsync(BlobReference reference);
}

// BAD: Mocking requires conditional logic inside the test double
public interface IStorageClient
{
    Task<HttpResponseMessage> SendAsync(HttpRequestMessage request);
}
```

**3. Example FakeItEasy setup**

Prefer a hand-written fake for boundaries you hit in many tests — it centralises behaviour and keeps tests readable:

```csharp
public class FakeContractStorageClient : IContractStorageClient
{
    private readonly Dictionary<string, Stream> _blobs = new();

    public Task<BlobReference> UploadAsync(Guid tenantId, Stream content, string fileName)
    {
        var reference = new BlobReference($"{tenantId}/contracts/{fileName}");
        _blobs[reference.Path] = content;
        return Task.FromResult(reference);
    }

    public Task<Stream> DownloadAsync(BlobReference reference) =>
        Task.FromResult(_blobs[reference.Path]);

    public Task DeleteAsync(BlobReference reference)
    {
        _blobs.Remove(reference.Path);
        return Task.CompletedTask;
    }
}
```

```csharp
public class ContractServiceTests
{
    private readonly FakeContractStorageClient _storage = new();
    private readonly ContractService _sut;

    public ContractServiceTests()
    {
        _sut = new ContractService(_storage);
    }

    [Fact]
    public async Task Upload_StoresBlobAndReturnsReference()
    {
        var result = await _sut.UploadContractAsync(tenantId, stream, "doc.pdf");

        Assert.Contains("doc.pdf", result.BlobReference.Path);
    }
}
```

For one-off boundary fakes where a full hand-written class would be over-engineered, use `A.Fake<T>()`:

```csharp
var emailSender = A.Fake<IEmailSender>();
A.CallTo(() => emailSender.SendAsync(A<Email>.Ignored)).Returns(Task.CompletedTask);
```

The narrow interface means:
- Each fake returns one specific shape
- No conditional logic in test setup
- Clear which external calls a test exercises
