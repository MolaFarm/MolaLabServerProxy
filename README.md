# 服务器代理程序
## 简述
这是一个与服务器建立通讯隧道进行通讯的程序，设计用于在内外网中以代理方式访问实验室服务器，程序实现了 `SOCKS-UNDER-QUIC` 通信协议，在单一通信隧道中可以建立复数通信流（具体可以建立多少个通信流取决于服务器与客户端的设置）

## 通信协议
服务器与客户端间的通信完全在**QUIC**通信隧道中进行，这是我们使用的底层通信协议。
**QUIC**是**RFC9000**通信规范中定义的新型通信规范，全称为**Quick UDP Internet Connection**，新的规范将使用**UDP**通信协议作为底层通信协议，能够实现与**TCP**相同的可靠通信能力，同时也带来了**UDP**的高效性能，以往的**TCP**通信为了保障通信可靠性，需要频繁进行握手，而且连接也无法进行复用，一个连接只能打开一个流，为通信带来了非常大的开销，而**QUIC**协议实现了多路复用能力，可以复用一个连接开启多个通信流，解决了频繁握手与连接建立的问题（特别是现代TLS加密通信的握手开销非常大），我们使用的**QUIC**协议也**不是标准**的，由于**QUIC**协议本身的发展还非常不成熟，我们需要对其进行修改以符合我们的需求：

标准协议无法做到和**TCP**相同的连接健康检测能力，我们修改了**QUIC**协议，标准握手流程完成后客户端与服务器间将打开连接中的**第一个**流进行均衡心跳握手，通过心跳握手实现了**TCP**中的连接健康检测能力

## 代码结构

- **Protocal**：通信协议实现 
- **Server**：服务端
- **ServerProxy**：客户端
- **ToastNotification**：Windows 系统消息推送模块
- **Updater**：客户端更新程序

## TODO
- Protocol
  - [ ] 完善心跳数据包机制[在连接繁忙时（通常为大规模上传下载操作）可能会出现心跳数据包传输失败，导致连接中断]
  - [ ] 添加 UDP 数据包转发及 Bind 请求转发的支持
- ServerProxy
  - [ ] 添加客户端设置系统代理的功能
  - [ ] 部分功能需要使用管理员权限[如证书安装]，但这些功能并非程序工作所需要持续运行的，需要添加按需申请管理员权限的能力
  - [ ] 根据巨硬文档，**QUIC**协议不支持 Windows 10，因而项目中引入了 OpenSSL 实现（Microsoft QUIC），需要在 Windows 10 下测试程序是否能正常工作
- Server
  - [ ] 编写 **SystemD Unit** 并将程序部署

## 许可协议
本程序在 `MIT` 协议下许可发布