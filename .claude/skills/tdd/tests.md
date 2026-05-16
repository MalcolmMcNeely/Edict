# Good and Bad Tests

Use **xunit** for all tests. Use **Verify** for snapshot/approval testing of complex outputs and Blazor component rendering. Use plain `Assert.*` — FluentAssertions is banned (commercial licence).

## Good Tests

**Integration-style**: Test through real interfaces, not mocks of internal parts.

```csharp
// GOOD: Tests observable behavior
[Fact]
public async Task Checkout_WithValidCart_ReturnsConfirmed()
{
    var cart = CreateCart();
    cart.Add(product);

    var result = await _checkoutService.CheckoutAsync(cart, paymentMethod);

    Assert.Equal("confirmed", result.Status);
}
```

Characteristics:

- Tests behavior users/callers care about
- Uses public API only
- Survives internal refactors
- Describes WHAT, not HOW
- One logical assertion per test

### Snapshot testing with Verify

**Use Verify on first write. Do not write `Assert.Equal` chains first and add Verify later — that wastes tokens.**

Use Verify whenever the return value has more than one field to assert. Use plain `Assert` only for single scalar checks (e.g. `Assert.True(result.IsSuccess)`). Ignore non-deterministic members (Guids, timestamps) with `.IgnoreMembersWithType<T>()`. If a Guid is semantically important (e.g. OrganizationId ownership), keep it as a separate `Assert.Equal` alongside the snapshot.

Use Verify for complex return values, serialised output, or Blazor component HTML where hand-writing every `Assert.Equal` would be brittle:

```csharp
[Fact]
public async Task ContractSummary_MatchesSnapshot()
{
    var summary = await _contractService.GetSummaryAsync(contractId);

    await Verify(summary).IgnoreMembersWithType<Guid>().IgnoreMembersWithType<DateTimeOffset>();
}
```

```csharp
// Blazor component snapshot with bUnit + Verify
[Fact]
public async Task ContractCard_RendersCorrectly()
{
    using var ctx = new TestContext();
    var cut = ctx.RenderComponent<ContractCard>(p => p
        .Add(c => c.ContractId, 42)
        .Add(c => c.Title, "Sample Contract"));

    await VerifyBunit(cut);
}
```

On first run, Verify creates a `.received.txt` file. Copy it to `.verified.txt` to accept. Subsequent runs diff against it — only update the snapshot when the change is intentional. Never commit `.received.*` files.

## Bad Tests

**Implementation-detail tests**: Coupled to internal structure.

```csharp
// BAD: Tests implementation details
[Fact]
public async Task Checkout_CallsPaymentService()
{
    var mockPayment = Substitute.For<IPaymentService>();
    await _checkoutService.CheckoutAsync(cart, payment);
    await mockPayment.Received().ProcessAsync(cart.Total); // testing HOW, not WHAT
}
```

Red flags:

- Mocking internal collaborators
- Testing private methods
- Asserting on call counts/order
- Test breaks when refactoring without behavior change
- Test name describes HOW not WHAT
- Verifying through external means instead of interface

```csharp
// BAD: Bypasses interface to verify
[Fact]
public async Task CreateUser_SavestoDatabase()
{
    await _userService.CreateAsync(new CreateUserRequest { Name = "Alice" });
    var row = await _db.Users.FirstOrDefaultAsync(u => u.Name == "Alice"); // leaks DB internals
    Assert.NotNull(row);
}

// GOOD: Verifies through interface
[Fact]
public async Task CreateUser_MakesUserRetrievable()
{
    var user = await _userService.CreateAsync(new CreateUserRequest { Name = "Alice" });
    var retrieved = await _userService.GetAsync(user.Id);
    Assert.Equal("Alice", retrieved.Name);
}
```
