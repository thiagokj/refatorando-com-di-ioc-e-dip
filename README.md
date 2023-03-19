# Refatorando com DI, IoC e DIP

Continuação do [projeto](https://github.com/thiagokj/injecao-de-dependencia-conceitual-csharp) de estudos de Inversão de Controle com Injeção de Dependência.

## Processo de rescrita de código e refatoração

Aqui devemos analisar e organizar o código, de forma a externalizar as dependências para repositórios e serviços.

```csharp
public class OrderController : ControllerBase
{
    [Route("v1/orders")]
    [HttpPost]
    public async Task<IActionResult> Place(string customerId, string zipCode, string promoCode, int[] products)
    {
        // #1 - Recupera o cliente
        // Podemos externalizar o cliente para um ClienteRepositorio
        Customer customer = null;
        await using (var conn = new SqlConnection("CONN_STRING"))
        {
            const string query = "SELECT [Id], [Name], [Email] FROM CUSTOMER WHERE ID=@id";
            customer = await conn.QueryFirstAsync<Customer>(query, new { id = customerId });
        }

        // #2 - Calcula o frete
        decimal deliveryFee = 0;
        var client = new RestClient("https://consultafrete.io/cep/");
        var request = new RestRequest()
            .AddJsonBody(new
            {
                zipCode
            });
        deliveryFee = await client.PostAsync<decimal>(request, new CancellationToken());
        // Nunca é menos de R$ 5,00
        if (deliveryFee < 5)
            deliveryFee = 5;

        // #3 - Calcula o total dos produtos
        decimal subTotal = 0;
        const string getProductQuery = "SELECT [Id], [Name], [Price] FROM PRODUCT WHERE ID=@id";
        for (var p = 0; p < products.Length; p++)
        {
            Product product;
            await using (var conn = new SqlConnection("CONN_STRING"))
                product = await conn.QueryFirstAsync<Product>(getProductQuery, new { Id = p });

            subTotal += product.Price;
        }

        // #4 - Aplica o cupom de desconto
        decimal discount = 0;
        await using (var conn = new SqlConnection("CONN_STRING"))
        {
            const string query = "SELECT * FROM PROMO_CODES WHERE CODE=@code";
            var promo = await conn.QueryFirstAsync<PromoCode>(query, new { code = promoCode });
            if (promo.ExpireDate > DateTime.Now)
                discount = promo.Value;
        }

        // #5 - Gera o pedido
        var order = new Order();
        order.Code = Guid.NewGuid().ToString().ToUpper().Substring(0, 8);
        order.Date = DateTime.Now;
        order.DeliveryFee = deliveryFee;
        order.Discount = discount;
        order.Products = products;
        order.SubTotal = subTotal;

        // #6 - Calcula o total
        order.Total = subTotal - discount + deliveryFee;

        // #7 - Retorna
        return Ok(new
        {
            Message = $"Pedido {order.Code} gerado com sucesso!"
        });
    }
}
```

## Criando repositórios

Exemplo de criação de um repositório básico chamado de CustomerRepository

```csharp
// Trecho a ser refatorado do controller
// #1 - Recupera o cliente
Customer customer = null;
await using (var conn = new SqlConnection("CONN_STRING"))
{
    const string query = "SELECT [Id], [Name], [Email] FROM CUSTOMER WHERE ID=@id";
    customer = await conn.QueryFirstAsync<Customer>(query, new { id = customerId });
}

// Externalizando para o CustomerRepository
public class CustomerRepository
{
    // Variáveis do tipo privada e somente leitura são instanciadas apenas uma única vez.
    // Precisamos criar essa variável para chama-la nos demais métodos que farão acesso ao banco.
    private readonly SqlConnection _connection;

    // Cria o construtor com a dependência de um SqlConnection,
    // atribuindo a conexão padrão.
    public CustomerRepository(SqlConnection connection)
        => _connection = connection;

    // Retorna o cliente conforme o base no Id informado.
    public async Task<Customer?> GetByIdAsync(string customerId)
    {
        const string query = "SELECT [Id], [Name], [Email] FROM CUSTOMER WHERE ID=@id";

        // Método padrão do Dapper.
        return await _connection
            .QueryFirstOrDefaultAsync<Customer>(
            query, new { id = customerId }
        );
    }
}
```

O ideal é sempre iniciar a criação pela **interface**, esse foi um exemplo para entender o fluxo.

## Aplicando o Princípio de Injeção de Dependência DIP

Por convenção no .NET, crie interfaces sempre utilizando o prefixo "I". Como as interfaces são como "contratos", pode ser criada uma estrutura de pastas no projeto chamada de **Contracts**.

Toda interface possui somente **assinatura de métodos**, então o escopo privado, publico, etc... não é aplicado.

```csharp
public interface ICustomerRepository
{
    Task<Customer?> GetByIdAsync(string customerId);
}

// Voltando ao CustomerRepository para informar que o repositório é uma implementação
// da interface ICustomerRepository
public class CustomerRepository : ICustomerRepository
{
...
}
```

Retornando ao OrderController para refatorar, os passos são similares para injetar os repositórios via construtor.

```csharp
public class OrderController : ControllerBase
{
    private readonly ICustomerRepository _customerRepository;

    public OrderController(ICustomerRepository customerRepository)
    {
        _customerRepository = customerRepository;
    }

    [Route("v1/orders")]
    [HttpPost]
    public async Task<IActionResult> Place(string customerId, string zipCode, string promoCode, int[] products)
    {
        // #1 - Recupera o cliente
        // Ficou muito mais simples agora. O exemplo abaixo tem apenas um retorno básico para
        // demostração.
        var customer = await _customerRepository.GetByIdAsync(customerId);
        if (customer == null)
        {
            return NotFound();
        }
    ...
}
```

## Serviços

Enquanto os repositórios tratam dados locais e acesso a dados, os serviços tratam itens externos.

```csharp
// Trecho a ser refatorado para um serviço
// #2 - Calcula o frete
decimal deliveryFee = 0;
var client = new RestClient("https://consultafrete.io/cep/");
var request = new RestRequest()
    .AddJsonBody(new
    {
        zipCode
    });
deliveryFee = await client.PostAsync<decimal>(request, new CancellationToken());
// Nunca é menos de R$ 5,00
if (deliveryFee < 5)
    deliveryFee = 5;

// Criando a interface do serviço de taxa de entrega
public interface IDeliveryFeeService
{
    Task<decimal> GetDeliveryFeeAsync(string zipCode);
}

// Fazendo a implementação do serviço de taxa de entrega
public class DeliveryFeeService : IDeliveryFeeService
{
    public async Task<decimal> GetDeliveryFeeAsync(string zipCode)
    {
        var client = new RestClient("https://consultafrete.io/cep/");
        var request = new RestRequest()
            .AddJsonBody(new
            {
                zipCode
            });

        var response = await client.PostAsync<decimal>(request);
        return response < 5 ? 5 : response;
    }
}

// Aplicando a refatoração no OrderController
// #2 - Calcula o frete
var deliveryFee = await _deliveryFeeService.GetDeliveryFeeAsync(zipCode);
```

A mesma refatoração aplicada para retornar o cliente, é aplicada para retornar o código promocional.
Agora é o momento de mover as regras de negócio do controlador e para as Models (Entidades).

Refatorando a entidade Order

```csharp
public class Order
{
    // No construtor do pedido passamos a taxa, desconto e os produtos do pedido
    public Order(
        decimal deliveryFee,
        decimal discount,
        List<Product> products)
    {
        Code = Guid.NewGuid().ToString().ToUpper().Substring(0, 8);
        Date = DateTime.Now;
        DeliveryFee = deliveryFee;
    }

    public string Code { get; set; }
    public DateTime Date { get; set; }
    public decimal DeliveryFee { get; set; }
    public decimal Discount { get; set; }
    public List<Product> Products { get; set; }

    // Nas propriedades retornamos os cálculos de soma dos produtos e o total do pedido.
    public decimal SubTotal => Products.Sum(x => x.Price);
    public decimal Total => SubTotal - Discount + DeliveryFee;
}
```

Agora simplificando todo o fluxo sem as regras de negócio no controller:

```csharp
public class OrderController : ControllerBase
{
    private readonly ICustomerRepository _customerRepository;
    private readonly IDeliveryFeeService _deliveryFeeService;
    private readonly IPromoCodeRepository _promoCodeRepository;

    public OrderController(
        ICustomerRepository customerRepository,
        IDeliveryFeeService deliveryFeeService,
        IPromoCodeRepository promoCodeRepository)
    {
        _customerRepository = customerRepository;
        _deliveryFeeService = deliveryFeeService;
        _promoCodeRepository = promoCodeRepository;
    }

    [Route("v1/orders")]
    [HttpPost]
    public async Task<IActionResult> Place(
        string customerId,
        string zipCode,
        string promoCode,
        int[] products)
    {
        var customer = await _customerRepository.GetByIdAsync(customerId);
        if (customer == null)
        {
            return NotFound();
        }
        var deliveryFee = await _deliveryFeeService.GetDeliveryFeeAsync(zipCode);
        var promo = await _promoCodeRepository.GetPromoCodeAsync(promoCode);
        var discount = promo?.Value ?? 0M; // Se tiver desconto retorna o valor, se não retorna 0.
        var order = new Order(deliveryFee, discount, new List<Product>());

        return Ok($"Pedido {order.Code} gerado com sucesso!");
    }
}
```

## Resolvendo Dependências

Nesse momento, informamos no Builder quais são as dependências para executar a aplicação.

```csharp
var builder = WebApplication.CreateBuilder(args);

// Carrega apenas uma única instância na memória. Só pode ser alterado ao reiniciar a aplicação.
builder.Services.AddSingleton<Configuration>();

// Garante apenas uma única instancia de conexão com o banco por requisição.
builder.Services.AddScoped<SqlConnection>();

// Sempre cria uma instancia nova de cada objeto ao chamar o construtor.
builder.Services.AddTransient<ICustomerRepository, CustomerRepository>();
builder.Services.AddTransient<IPromoCodeRepository, PromoCodeRepository>();
builder.Services.AddTransient<IDeliveryFeeService, DeliveryFeeService>();
```

## Métodos de Extensão

Os Extension Methods permitem adicionar comportamentos a qualquer classe, inclusive as classes padrão do .NET.

Uma boa prática é criar um método de extensão para o WebApplicationBuilder, organizando as dependências no builder.

Para isso é necessário criar uma **classe e métodos estáticos** e usar a palavra chave **this** seguida do nome da classe/interface de origem.

```csharp
public static class DependenciesExtension
{
    public static void AddConfiguration(this IServiceCollection services)
    {
        services.AddSingleton<Configuration>();
    }

    public static void AddSqlConnection(
        this IServiceCollection services,
        WebApplicationBuilder builder)
    {
        services.AddScoped<SqlConnection>(
            x => new SqlConnection(
                builder
                    .Configuration
                    .GetConnectionString("DefaultConnection"))
        );
    }

    public static void AddRepositories(this IServiceCollection services)
    {
        services.AddTransient<ICustomerRepository, CustomerRepository>();
        services.AddTransient<IPromoCodeRepository, PromoCodeRepository>();
    }

    public static void AddServices(this IServiceCollection services)
    {
        services.AddTransient<IDeliveryFeeService, DeliveryFeeService>();
    }
}
```

Agora conseguimos encapsular todo o código da aplicação como extensões dos serviços, deixando bem limpa a inicialização.

```csharp
using DependencyStore.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddConfiguration();
builder.Services.AddSqlConnection(builder);
builder.Services.AddRepositories();
builder.Services.AddServices();
builder.Services.AddControllers();

var app = builder.Build();

app.MapControllers();

app.Run();
```

Para melhorar a forma de resolver dependências, temos os métodos **TryAdd**, que evitam duplicidade.

## Formas de resolver dependências

**Resolvendo no construtor** -> Criando uma variável privada somente leitura, que é atribuída somente via construtor.
Obs: no caso de constantes (const), só é possível atribuir o valor na sua declaração.

```csharp
private readonly IWeatherService _service;

public WeatherController(IWeatherService service)
    => _service = service;

[HttpGet("/")]
public IEnumerable<WeatherForecast> Get()
    => _service.Get();
```

**Resolvendo com o FromServices** -> Caso a dependência seja usada em apenas um método, é possível reduzir a quantidade de código para indicar a resolução da dependência. Caso haja dependência em vários métodos, o melhor cenário é usar o construtor.

```csharp
// A partir do .NET7, não é necessário declarar o FromServices, o .NET7 tenta resolver automaticamente.
[HttpGet("/")]
public IEnumerable<WeatherForecast> Get(
    [FromServices] IWeatherForecast service
)
    => service.Get();
```

**Resolvendo no Program.cs** -> Há uma forma de fazer na classe principal. Após criar a aplicação, podemos usar a declaração similar a abaixo:

```csharp
var app = builder.Build();

// Retorna um ServiceScope
using(var scope = app.Services.CreateScope())
{
    // Retorna dos os serviços registrados
    var services = scope.ServiceProvider;

    var repository = services.GetRequiredService<ICustomerRepository>();
    repository.CreateAsync(new Customer("Thiago"));
}
```

**Resolvendo com o HttpContext** -> Recuperando os serviços via HttpContext, quando estamos fora do Controller ou do Program.cs. Usado quando necessário retornar a instancia de um middleware.

```csharp
public async Task OnActionExecutionAsync(
    ActionExecutingContext context,
    ActionExecutionDelegate next
)
{
    // O serviço já deve estar registrado, evitando o retorno nulo.
    var service = context
        .HttpContext
        .RequestServices
        .GetService<IWeatherService>();

    var forecast = service.Get();
}
```
