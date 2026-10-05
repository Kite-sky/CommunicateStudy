

[TOC]

# 一、Modbus简介

Modbus 是工业领域最通用的**串行总线通信协议**，由 Modicon 公司推出，属于**应用层协议**，可以跑在 RS485、RS232、以太网之上。核心特点：**主从问答式**，一主多从。

## **1.1 核心通信模型-主从（Master-Slave）**

1. 主站（Master）：发起请求，主动发送指令。
   - 例：上位机、PLC 主机、触摸屏
   - 只能主站发起通信，**从站不会主动发消息**
2. 从站（Slave）：被动等待主站查询，收到合法请求后回复响应帧。
   - 例：传感器、温控仪表、变频器、IO 模块
   - 每个从站有唯一**从站地址（Slave ID）**，范围 1~247；0 为广播地址（所有从站接收，**不回复**）

## 1.2 数据类型

数据方面，Modbus 支持两种基本格式：

- **离散量**：也就是单个 bit，表示开关状态，比如 0/1、真/假。
- **寄存器**：16 位字，通常存数值，比如温度、压力，或者其他需要表达的复杂数据。

## 1.3 数据传输

Modbus 按 **先低位后高位** 的顺序发送比特（LSB to MSB）。

# 二、三种传输模式对比

| 项目     | Modbus RTU         | Modbus ASCII                   | Modbus TCP                       |
| -------- | ------------------ | ------------------------------ | -------------------------------- |
| 底层链路 | RS485/RS232 串口   | RS485/RS232 串口               | Ethernet 以太网                  |
| 编码方式 | 原始二进制         | 每个字节转为 2 个 ASCII 字符   | 二进制                           |
| 校验     | CRC16              | LRC 纵向校验                   | TCP 自带校验，无 Modbus 层校验   |
| 帧分隔   | 空闲间隔 T3.5 字符 | 起始`:`，结束 0x0D 0x0A (CRLF) | TCP 流，依靠 MBAP 长度字段拆分   |
| 地址     | SlaveID(1~247)     | SlaveID(1~247)                 | UnitID（等效 SlaveID，用于网关） |
| 端口     | 无（串口）         | 无（串口）                     | 默认 502                         |
| 传输效率 | 高，工程最常用     | 低，报文长，调试看报文方便     | 高，以太网上位机首选             |
| 适用场景 | 现场总线仪表       | 早期调试，现已淘汰             | 网口 PLC、远程采集               |

# 三、Modbus寄存器模型

Modbus 的通信离不开 **地址** 和 **功能码**

Modbus 将从站设备的数据抽象成 4 种独立存储区，分为**位（bit）**和**字（16bit）**两种类型。

> ⚠️ 两套地址体系：
>
> 1. **协议地址**：协议标准定义，从 0 开始（代码、驱动内部使用）
> 2. **厂商文档显示地址**：从 1 开始，仪表手册常用，**代码使用必须减 1**

## 3.1 设备地址

每个从设备有独一无二地址，通常为1-247。主站发送数据带有从站号，确保数据到达对应的从站。

## 3.2 寄存器地址

Modbus 里的数据按类型存放在不同寄存器里，常见的有：

| 存储区     | 英文名称          | 类型     | 读写权限 | 协议起始地址 | 手册显示地址 | 典型用途                      |
| ---------- | ----------------- | -------- | -------- | ------------ | ------------ | ----------------------------- |
| 线圈       | Coils             | Bit 位   | 读 / 写  | 00000        | 00001        | DO 数字输出：继电器、电磁阀   |
| 离散输入   | Discrete Inputs   | Bit 位   | 只读     | 10000        | 10001        | DI 数字输入：按钮、接近开关   |
| 保持寄存器 | Holding Registers | 16bit 字 | 读 / 写  | 40000        | 40001        | 参数、设定值、输出模拟量      |
| 输入寄存器 | Input Registers   | 16bit 字 | 只读     | 30000        | 30001        | AI 采集：温度、压力原始采集值 |

> 关键特性：1 个寄存器 = **16bit = 2 字节**；线圈 / 离散输入：1 个地址对应 1bit，连续读取时会打包成字节传输（低位在前）。

示例：手册写保持寄存器 40005

> 协议地址 = 40005 - 1 = **40004**，代码请求起始地址填 40004。(常用)

## 3.3 功能码

| 功能码 (十进制) | 功能码 (十六进制) | 名称                      | 作用             | 读写对象                           | 单次最大访问数量 |
| --------------- | ----------------- | ------------------------- | ---------------- | ---------------------------------- | ---------------- |
| 1               | 0x01              | Read Coils                | 读线圈           | 可读写开关输出 Coils (bit)         | 2000 点          |
| 2               | 0x02              | Read Discrete Inputs      | 读离散输入       | 只读开关输入 Discrete Inputs (bit) | 2000 点          |
| 3               | 0x03              | Read Holding Registers    | 读保持寄存器     | 可读写 16 位寄存器 Holding Reg     | 125 个寄存器     |
| 4               | 0x04              | Read Input Registers      | 读输入寄存器     | 只读 16 位采集寄存器 Input Reg     | 125 个寄存器     |
| 5               | 0x05              | Force Single Coil         | 写单个线圈       | 单点线圈 (bit)                     | 1 点             |
| 6               | 0x06              | Preset Single Register    | 写单个保持寄存器 | 单个保持寄存器 (16bit)             | 1 个寄存器       |
| 15              | 0x0F              | Force Multiple Coils      | 写多个线圈       | 批量线圈 (bit)                     | 1968 点          |
| 16              | 0x10              | Preset Multiple Registers | 写多个保持寄存器 | 批量保持寄存器 (16bit)             | 123 个寄存器     |

# 四、协议详解

## 4.1 Modbus-RTU协议⭐

**帧结构**

> **帧结构 = 地址 + 功能码+ 数据 + 校验**

![](D:\Workspace\CommunicateStudy\img\ModbusRTUFrame.png)

> **Coding System**
> Eight-bit binary, hexadecimal 0 ... 9, A ... F
> Two hexadecimal characters contained in each eight-bit field of the message
> **Bits per Byte**
> 1 start bit
> 8 data bits, least significant bit sent first
> 1 bit for even / odd parity-no bit for no parity
> 1 stop bit if parity is used-2 bits if no parity
> **Error Check Field**
> Cyclical Redundancy Check (CRC)

## 4.2 Modbus-ASCII协议（使用的少）

![](D:\Workspace\CommunicateStudy\img\ModbusASCII.png)



## 4.3 Modbus-TCP协议⭐⭐⭐

![](D:\Workspace\CommunicateStudy\img\ModbusTCPFrame.png)

> TCP 模式是 Modbus 的以太网版本，基于 TCP/IP 协议，跑在标准以太网 infrastructure 上。相比串口，TCP 速度快、距离远，还能轻松接入企业网络，特别适合现代化的 SCADA 系统或远程监控。



> 参考连接：
>
> [(82 封私信 / 28 条消息) 一文讲透Modbus协议，超级详细！！！ - 知乎](https://zhuanlan.zhihu.com/p/1923537303634150103)
>
> [Modbus Protocol](https://www.modbustools.com/modbus.html)
>
> [Modbus教程 | Modbus中文网](https://www.modbus.cn/modbus-guide)
>
> [Modbus通讯协议从一窍不通到原来如此_modbus从站主动上报-CSDN博客](https://blog.csdn.net/qq_41965346/article/details/118760449)