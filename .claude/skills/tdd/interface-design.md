# Interface Design for Testability

Good interfaces make testing natural:

1. **Accept dependencies, don't create them**

   ```csharp
   // Testable — gateway is injected, easy to substitute
   public class OrderProcessor(IPaymentGateway gateway)
   {
       public Task ProcessAsync(Order order) => gateway.ChargeAsync(order.Total);
   }

   // Hard to test — infrastructure created inside domain logic
   public class OrderProcessor
   {
       public Task ProcessAsync(Order order)
       {
           var gateway = new StripeGateway(); // untestable
           return gateway.ChargeAsync(order.Total);
       }
   }
   ```

2. **Return results, don't mutate through side effects**

   ```csharp
   // Testable — result is explicit, easy to assert
   public DiscountResult CalculateDiscount(Cart cart) { ... }

   // Hard to test — side effect hidden inside, nothing to assert on
   public void ApplyDiscount(Cart cart)
   {
       cart.Total -= discount; // mutation, no return value
   }
   ```

3. **Small surface area**
   - Fewer methods = fewer tests needed
   - Fewer params = simpler test setup
   - Prefer `record` types for inputs/outputs — structural equality makes `Assert.Equal` work without custom comparers
