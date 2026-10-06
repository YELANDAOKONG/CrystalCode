---
name: csharp-clean-code
description: >
  Use this skill when writing, reviewing, refactoring, or discussing C# code. Covers
  file-scoped namespaces, using declarations, naming conventions, pattern matching,
  null checks, async/await best practices, comment guidelines, and more. Applies to
  any C# project (.NET Core, ASP.NET, Unity, MAUI, etc.). Trigger this skill even
  when the user does not explicitly mention "style guide" or "clean code" — any
  time C# code quality, readability, or best practices are relevant.
---

# C# Clean Code Style Guide

Rules are classified as **Mandatory** (must follow; violations should block merge)
or **Recommended** (strongly encouraged; exceptions need justification).

## Mandatory Rules

### 1. File-Scoped Namespaces

Always use file-scoped namespace declarations (C# 10+).

```csharp
// Correct
namespace Company.Product.Module;

// Incorrect: block-style adds unnecessary nesting
namespace Company.Product.Module
{
    // Code here
}
```

### 2. English for User-Facing Messages

Exception messages, log output, and UI text must be in English. Non-English
text can break console encoding and is inaccessible to international tooling.

```csharp
// Correct
throw new ArgumentException("Value cannot be null or empty.");

// Incorrect
throw new ArgumentException("値がnullまたは空であってはなりません。");
```

### 3. Naming Conventions

**Types and public members — PascalCase:**

```csharp
public class CustomerService
{
    public void ProcessOrder(Order order) { }
    public string FirstName { get; set; }
    public const int MaxRetries = 3;
}
```

**Local variables and parameters — camelCase:**

```csharp
public void ProcessOrder(Order order)
{
    var customerName = order.Customer.Name;
}
```

**Private fields — _camelCase (underscore prefix):**

Applies to both instance and static private fields.

```csharp
private int _count;
private static string _defaultConnectionString;
```

**Interfaces start with I:**

```csharp
public interface IOrderProcessor
{
    Task ProcessAsync(Order order);
}
```

### 4. Language Keywords Over Runtime Types

Use C# language keywords, not CLR type names.

```csharp
// Correct
string name;
int count;
long id;
float ratio;
double amount;
bool active;
char key;
nint handle;
nuint length;

// Incorrect
String name;
Int32 count;
Int64 id;
Single ratio;
Double amount;
Boolean active;
Char key;
```

### 5. Braces Required on All Control Flow

All `if`, `else`, `for`, `foreach`, `while`, `do`, and `using` blocks must use
braces, even for single statements. This prevents bugs when statements are
added later.

```csharp
// Correct
if (isValid)
{
    Process(order);
}

// Incorrect
if (isValid)
    Process(order);
```

### 6. Switch Must Have Default

Every `switch` statement and expression must handle all cases, either with
explicit cases or a `default` / `_` arm.

```csharp
// Correct
var result = status switch
{
    OrderStatus.Pending => "Pending",
    OrderStatus.Shipped => "Shipped",
    _ => "Unknown"
};

switch (status)
{
    case OrderStatus.Pending:
        HandlePending();
        break;
    default:
        throw new ArgumentOutOfRangeException(nameof(status));
}
```

### 7. No Top-Level Statements

Do not use top-level statements (C# 9+). Always use an explicit `Main` method
within a `Program` class. Top-level statements obscure the program entry point
and prevent explicit `async Task Main`.

```csharp
// Correct
namespace MyApp;

public static class Program
{
    public static async Task Main(string[] args)
    {
        await RunAsync(args);
    }
}

// Incorrect: top-level statements
Console.WriteLine("Hello");
await Task.Delay(1000);
```

### 8. Avoid Magic Numbers

Replace hardcoded numeric literals with named constants. Exceptions: `0`, `1`,
and `-1` in trivial contexts (loop counters, array indices, initial values) are
exempt.

```csharp
// Correct
const int MaxRetryCount = 3;
for (var i = 0; i < MaxRetryCount; i++) { }

// Incorrect
for (var i = 0; i < 3; i++) { }

// Allowed: 0, 1, -1 in obvious contexts
for (var i = 0; i < items.Length; i++) { }
var first = items[0];
items.Add(item, index: -1);
```

### 9. One Type Per File

Each file contains exactly one type. The file name must match the type name.

```
CustomerService.cs  →  public class CustomerService
IOrderRepository.cs →  public interface IOrderRepository
```

### 10. Using Directive Order

Order using directives in three groups separated by blank lines: System
namespaces first, then third-party libraries, then your own namespaces.

```csharp
using System;
using System.Collections.Generic;

using Microsoft.Extensions.Logging;

using Company.Core.Models;
```

### 11. Exceptions Only for Exceptional Conditions

Throw exceptions only for truly exceptional cases, never for normal control
flow. If a condition is expected (e.g., a search returning no results),
handle it without throwing.

```csharp
// Correct: exception guards an invalid precondition
if (string.IsNullOrEmpty(filePath))
    throw new ArgumentException("File path cannot be empty", nameof(filePath));

// Incorrect: using exceptions for expected conditions
try
{
    var data = GetData();
    ProcessData(data);
}
catch (DataNotFoundException)
{
    // Don't use exceptions for expected control flow
}
```

### 12. Include Parameter Name in ArgumentException

All `ArgumentException`, `ArgumentNullException`, and
`ArgumentOutOfRangeException` constructors must include the parameter name via
`nameof()`.

```csharp
// Correct
throw new ArgumentNullException(nameof(customer), "Customer cannot be null");
throw new ArgumentException("Invalid status value", nameof(status));

// Incorrect: missing parameter name
throw new ArgumentNullException("Customer cannot be null");
```

---

## Recommended Rules

### 13. Using Declarations

Prefer C# 8.0+ using declarations over using blocks.

```csharp
// Preferred: using declaration — scope is the enclosing block
using var reader = new StreamReader(path);
var content = reader.ReadToEnd();

// Acceptable but less preferred
using (var reader = new StreamReader(path))
{
    var content = reader.ReadToEnd();
}
```

### 14. Expression-Bodied Members

Use `=>` for simple single-expression methods and properties.

```csharp
// Preferred when simple
public string GetFullName() => $"{FirstName} {LastName}";
public bool IsValid => !string.IsNullOrEmpty(Name);

// Acceptable for multi-step logic
public string GetFullName()
{
    var normalized = Normalize(FirstName);
    return $"{normalized} {LastName}";
}
```

### 15. Pattern Matching

Use pattern matching instead of separate type-checking and casting.

```csharp
// Preferred
if (obj is Customer { IsActive: true } customer)
{
    // Use customer directly
}

var result = obj switch
{
    Customer c => c.Name,
    Order o => o.Id,
    _ => "Unknown"
};

// Acceptable but less preferred
if (obj is Customer && ((Customer)obj).IsActive)
{
    var customer = (Customer)obj;
}
```

### 16. Null-Conditional and Null-Coalescing Operators

Use `?.`, `??`, and `?[]` for concise null handling.

```csharp
// Preferred
var name = customer?.Name ?? "Unknown";
var count = orders?.Count ?? 0;
var firstItem = list?[0];
```

### 17. Interpolated Strings

Use `$""` string interpolation over concatenation or `string.Format`.

```csharp
// Preferred
var message = $"Hello {name}, you have {count} items.";

// Acceptable for localized format strings loaded from resources
var message = string.Format(formatString, name, count);

// Avoid
var message = "Hello " + name + ", you have " + count + " items.";
```

### 18. Object and Collection Initializers

Favor initializer syntax over separate assignment or Add calls.

```csharp
// Preferred
var customer = new Customer
{
    Name = "Alice",
    IsActive = true
};

var items = new List<int> { 1, 2, 3 };

// Acceptable but less preferred
var customer = new Customer();
customer.Name = "Alice";
customer.IsActive = true;
```

### 19. Collection Expressions (C# 12+)

Use `[]` collection expression syntax when targeting C# 12 or later.

```csharp
// Preferred (C# 12+)
int[] numbers = [1, 2, 3];
List<string> names = ["Alice", "Bob"];
Span<char> chars = ['a', 'b', 'c'];

// Acceptable for earlier C# versions
int[] numbers = new[] { 1, 2, 3 };
List<string> names = new() { "Alice", "Bob" };
```

### 20. Use nameof for Hard References

Use `nameof()` instead of hardcoded strings when referencing code elements.

```csharp
// Preferred
throw new ArgumentNullException(nameof(order));
Logger.LogInformation("{Method} completed", nameof(ProcessOrder));

// Avoid
throw new ArgumentNullException("order");
```

### 21. Record for Immutable Data

Use `record` (or `record struct`) for types whose instances are immutable and
compared by value. Use `class` for mutable objects with identity semantics.

```csharp
// Preferred: immutable data with value equality
public record CustomerDto(int Id, string Name, string Email);

// Preferred: mutable object with behavior and identity
public class CustomerService
{
    private readonly IRepository _repository;
    // ...
}
```

### 22. Proper Async/Await

Always await async calls; never use `.Result` or `.Wait()`. Async method names
should end with `Async`.

```csharp
// Correct
public async Task<Result> ProcessOrderAsync(Order order)
{
    var data = await _repository.GetAsync(order.Id);
    return await _processor.RunAsync(data);
}

// Incorrect: blocks the calling thread
public async Task<Result> ProcessOrderAsync(Order order)
{
    var data = _repository.GetAsync(order.Id).Result;
    return _processor.RunAsync(data).Result;
}
```

### 23. ConfigureAwait(false) in Libraries

Use `ConfigureAwait(false)` in library projects to avoid deadlocks. In
application code (ASP.NET controllers, MAUI, etc.) it is usually unnecessary.

```csharp
// Library code
await httpClient.GetAsync(url).ConfigureAwait(false);

// Application code
await httpClient.GetAsync(url);
```

### 24. Code Clarity Over Cleverness

Write code that is obvious to the next reader. Avoid unnecessarily compact or
tricky constructs.

```csharp
// Preferred: clear and explicit
var activeCustomers = customers.Where(c => c.IsActive).ToList();

// Avoid: too clever
var actives = customers.Where(c => c switch { { IsActive: true } => true, _ => false });
```

### 25. Comments Explain Why, Not What

Code should be self-documenting for the "what". Comments should explain the
"why" — motivation, constraints, edge cases.

```csharp
// Correct: explains the reason
// Skip validation for system accounts so they can use special characters
if (!account.IsSystemAccount)
{
    ValidateUsername(account.Username);
}

// Incorrect: restates what the code already says
// Check if not a system account
if (!account.IsSystemAccount)
{
    // Validate the username
    ValidateUsername(account.Username);
}
```

### 26. Avoid Double Negatives

Write conditions in the positive form when possible. Double negatives force the
reader to mentally invert the logic.

```csharp
// Preferred: positive form
if (isActive)
{
    Process();
}

if (!isValid)
{
    return;
}

// Avoid: double negative
if (!isInactive)
{
    Process();
}
```

### 27. Avoid Nested Loops

Extract inner loops into separate methods, or flatten with LINQ. Deeply nested
loops are hard to read and test.

```csharp
// Preferred: extract method
foreach (var order in orders)
{
    ProcessOrderItems(order.Items);
}

void ProcessOrderItems(IEnumerable<Item> items)
{
    foreach (var item in items)
    {
        // ...
    }
}

// Preferred: flatten with LINQ
var activeItems = orders
    .SelectMany(o => o.Items)
    .Where(i => i.IsActive);

// Acceptable when the logic is trivial and extracting would hurt readability
foreach (var row in grid)
{
    foreach (var cell in row)
    {
        total += cell.Value;
    }
}
```

### 28. Declare Variables as Late as Possible

Declare variables near their first use, not at the top of the method. This
reduces the reader's mental tracking burden and narrows scope.

```csharp
// Preferred
var customer = _repository.GetCustomer(id);
var order = customer.LatestOrder;
Process(order);

// Avoid: declaring everything upfront
var customer = null as Customer;
var order = null as Order;
customer = _repository.GetCustomer(id);
order = customer.LatestOrder;
Process(order);
```

### 29. Assign Each Variable in a Separate Statement

Declare one variable per line. This makes diffs cleaner and declarations easier
to scan.

```csharp
// Preferred
var name = customer.Name;
var age = customer.Age;

// Avoid
var name = customer.Name, age = customer.Age;
```

### 30. Avoid ref and out Parameters

Use return values, tuples, or result objects instead. `ref` and `out` make call
sites harder to understand and prevent method chaining.

```csharp
// Preferred: return a result type
var result = TryParse(input);

// Preferred: return a tuple
var (success, value) = int.TryParseModern(input);

// Acceptable: standard TryParse pattern (pervasive in .NET BCL)
if (int.TryParse(input, out var value))
{
    // This is fine — it is the established .NET convention
}
```

### 31. Avoid Commented-Out Code

Do not leave commented-out code in production. During active development it is
tolerable, but it should be removed before merging. Git history preserves the
old code; comments do not.

```csharp
// Correct: code is either live or deleted
if (isActive)
{
    Process();
}

// Tolerated during development only — remove before merge
// if (isLegacy)
// {
//     OldProcess();
// }
```

---

## Quick Review Checklist

Check in order:

1. Namespace is file-scoped?
2. Messages and exceptions are in English?
3. Types = PascalCase, locals/params = camelCase, private fields = `_camelCase`, interfaces = `I` prefix?
4. `string` not `String`, `int` not `Int32`?
5. All `if`/`for`/`while` have braces?
6. Every `switch` has a `default` case?
7. No top-level statements; explicit `Main` method?
8. No magic numbers (except 0/1/-1 in trivial contexts)?
9. One type per file, usings ordered System → third-party → own?
10. Exceptions only for exceptional conditions, with `nameof(param)`?
11. Using declarations for `IDisposable`?
12. Simple members use expression bodies?
13. Pattern matching over type-check-and-cast?
14. `?.` / `??` / `?[]` for null checks?
15. Interpolated strings over concatenation?
16. Object/collection initializers over separate assignments?
17. Collection expressions `[...]` if on C# 12+?
18. `nameof()` over hardcoded strings?
19. `record` for immutable data, `class` for mutable objects?
20. `async`/`await` correct, no `.Result` / `.Wait()`?
21. `ConfigureAwait(false)` in library code?
22. Code clear and direct?
23. Comments explain why?
24. Conditions written in positive form (no double negatives)?
25. Nested loops extracted or flattened?
26. Variables declared close to first use?
27. One variable per declaration?
28. No ref/out parameters (except standard TryParse)?
29. No commented-out code remaining?
