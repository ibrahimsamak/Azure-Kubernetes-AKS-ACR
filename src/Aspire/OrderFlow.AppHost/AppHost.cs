var builder = DistributedApplication.CreateBuilder(args);

// ---------- Infrastructure containers ----------
var kafka = builder.AddKafka("kafka")
                   .WithDataVolume()
                   .WithKafkaUI();

var rabbit = builder.AddRabbitMQ("rabbitmq")
                    .WithManagementPlugin(); 

var sql = builder.AddSqlServer("sql").WithDataVolume();

var ordersDb = sql.AddDatabase("orderflow-orders");
var inventoryDb = sql.AddDatabase("orderflow-inventory");
var paymentsDb = sql.AddDatabase("orderflow-payments");
var notificationsDb = sql.AddDatabase("orderflow-notifications");

// No Redis: nothing reads it yet, and a provisioned dependency nobody uses costs startup
// time and reads like a contract that exists. Bring it back in one line when there is an
// actual cache or SignalR backplane to point at it.

// ---------- Services ----------
// Inventory first: Order's gRPC 
var inventory = builder.AddProject<Projects.OrderFlow_Inventory_Api>("inventory")
    .WithReference(kafka).WaitFor(kafka)
    .WithReference(inventoryDb).WaitFor(inventoryDb);

var payment = builder.AddProject<Projects.OrderFlow_Payment_Api>("payment")
    .WithReference(kafka).WaitFor(kafka)
    .WithReference(paymentsDb).WaitFor(paymentsDb);

var notification = builder.AddProject<Projects.OrderFlow_Notification_Api>("notification")
    .WithReference(kafka).WaitFor(kafka)
    .WithReference(rabbit).WaitFor(rabbit)
    .WithReference(notificationsDb).WaitFor(notificationsDb);

builder.AddProject<Projects.OrderFlow_Order_Api>("order")
    .WithReference(kafka).WaitFor(kafka)
    .WithReference(ordersDb).WaitFor(ordersDb)
    // This is the line that makes gRPC service discovery work: Order resolves
    // "https://inventory" at runtime instead of hard-coding a port.
    .WithReference(inventory).WaitFor(inventory)
    .WithReplicas(1);

builder.Build().Run();
