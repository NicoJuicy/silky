---
title: 分布式锁
lang: zh-cn
---

## 介绍

为了保证一个方法或属性在高并发情况下的同一时间只能被同一个线程执行，在传统单体应用单机部署的情况下，可以使用并发处理相关的功能进行互斥控制。但是，随着业务发展的需要，原单体单机部署的系统被演化成分布式集群系统后，由于分布式系统多线程、多进程并且分布在不同机器上，这将使原单机部署情况下的并发控制锁策略失效，单纯的应用并不能提供分布式锁的能力。为了解决这个问题就需要一种跨机器的互斥机制来控制共享资源的访问，这就是分布式锁要解决的问题。

分布式锁应该具备哪些条件？

1. 在分布式系统环境下，一个方法在同一时间只能被一个机器的一个线程执行；
2. 高可用的获取锁与释放锁；
3. 高性能的获取锁与释放锁；
4. 具备可重入特性；
5. 具备锁失效机制，防止死锁；
6. 具备非阻塞锁特性，即没有获取到锁将直接返回获取锁失败。

## silky框架的分布式锁

silky框架使用[RedLock.net](https://github.com/samcook/RedLock.net)实现分布式锁,RedLock.net使用redis服务实现的分布式锁。

silky框架在服务条目注册的过程中,使用到了分布式锁,避免由于多个服务实例同时注册统一服务条目,导致服务地址未被更新的问题。由于在框架层面使用了分布式锁,所以,在普通业务应用服务中,开发者必须要对分布式锁使用到的`redis`服务进行配置。

## 使用

1. 在配置文件中的`lock`节点指定redis服务的连接字符串

```yml
lock:
  lockRedisConnection: silky.redis1:6379,defaultDatabase=1
```

2. 通过构造器注入`ILockerProvider`的实例

```csharp
protected readonly ILockerProvider _lockerProvider;
protected ServiceRouteManagerBase(ILockerProvider lockerProvider)           
{
   _lockerProvider = lockerProvider;
}

```

3. 通过`ILockerProvider`实例对象创建分布式锁,并通`locker`执行锁定的代码块
  
   创建分布式锁对象的时候,需要传入一个锁定的资源名称。 
 
  ```csharp
  protected async Task RegisterRouteWithLockAsync(ServiceRouteDescriptor serviceRouteDescriptor)
  {
      using var locker = await _lockerProvider.CreateLockAsync(serviceRouteDescriptor.ServiceDescriptor.Id);
      await locker.Lock(async () => { await RegisterRouteAsync(serviceRouteDescriptor); });
  }
  ```