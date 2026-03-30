# Project DNA 连接与鉴权决策

> Status: Accepted
> Date: 2026-03-30
> Scope: Client / Server 连接协议、接口分层、鉴权边界

## 1. 决策结论

Project DNA 的多人协作版连接方案收敛为：

- 主业务通道使用 `HTTPS REST`
- 身份与权限使用 `JWT Bearer Token`
- 实时推送与流式输出使用 `SSE`
- `MCP` 仅作为 `Client` 对 IDE / Agent 的接入协议，不作为多人访问 `Server` 的主业务协议

这是一条明确的产品与架构边界：

- `Server` 是团队共享知识服务，负责账号、权限、审核、审计、正式知识库一致性
- `Client` 是每个开发者本地的宿主层，负责 Web UI、本地 MCP、后续内置 Agent，以及把本地用户操作转发到 `Server`

## 2. 为什么选择 REST + JWT + SSE

选择这套方案的原因：

1. 当前核心业务是账号权限、知识查询、提审、审核、发布、审计，这些都是标准请求-响应型业务，更适合 REST。
2. 团队多人访问场景下，REST 更适合接入反向代理、日志审计、统一鉴权、限流和后续网关治理。
3. `Server` 的知识治理天然存在明确的边界：正式知识只读、预审知识可编辑、管理员可发布。JWT + 角色模型比长连接协议更适合表达这些权限。
4. 需要实时能力的场景是局部的，不是全量业务：
   - 审核队列实时刷新
   - 发布结果通知
   - 内置 Agent 流式输出
   - 后台任务进度推送
5. 这些“服务端单向推送”场景优先用 SSE，复杂度和运维成本都低于 WebSocket。

## 3. 顶层拓扑

推荐的长期拓扑如下：

```text
IDE / External Agent
        |
        | MCP
        v
Local Client (per user)
        |
        | HTTPS REST + JWT
        v
Shared DNA Server
        |
        +-- Formal Knowledge Store
        +-- Review Submission Store
        +-- User / Role / Audit Store
```

浏览器有两条入口：

1. 普通用户工作台：`Browser -> Local Client -> Shared Server`
2. 管理员管理台：`Browser -> Shared Server`

这里的关键约定是：

- 团队共享入口永远是 `Server`
- `Client` 永远是本地宿主，不作为团队共享网关
- `Client` 默认只监听本机回环地址，避免误变成团队公共服务

## 4. 协议分工

### 4.1 REST

REST 负责所有有状态业务动作：

- 登录 / 获取当前用户
- 查询正式知识
- 提交预审知识
- 编辑 / 撤回自己的提审单
- 管理员审核、驳回、发布
- 管理员直写正式知识
- 用户管理、角色管理、审计查询

原则：

- 所有“要落库、要鉴权、要审计、要重试”的操作都走 REST
- SSE 不承担业务命令写入

### 4.2 JWT

JWT 负责在 `Server` 侧表达用户身份与角色。

原则：

- Token 只由 `Server` 签发与校验
- `Client` 不拥有独立权限体系，不解释角色语义，只负责保存和透传 token
- 所有权限裁决都在 `Server` 完成

推荐角色最小集合：

- `viewer`: 只读正式知识
- `editor`: 读取正式知识 + 提交 / 修改 / 撤回自己的预审单
- `admin`: 审核、发布、直写正式知识、用户管理

### 4.3 SSE

SSE 只负责服务端到客户端的单向流式推送：

- 审核队列变更事件
- 提审状态变更事件
- 发布完成事件
- 后续内置 Agent 的流式输出
- 后台任务进度 / 系统通知

原则：

- SSE 不替代 REST
- SSE 不承担强一致命令通道
- 如果未来出现“浏览器与服务端高频双向交互、多人实时协同编辑”再评估 WebSocket

## 5. 服务端接口分层

`Server` 对外接口分为五层。

### 5.1 认证层

路径前缀：

- `/api/auth/*`

职责：

- 登录
- 获取当前用户
- 管理用户与角色

约定：

- `POST /api/auth/login` 允许匿名访问
- `POST /api/auth/register` 只允许引导期开放；团队部署后应关闭公开注册，改为管理员创建或邀请机制
- 其余认证相关读取默认要求已登录

### 5.2 正式知识读取层

典型路径：

- `/api/status`
- `/api/topology`
- `/api/graph/*`
- `/api/memory/query`
- `/api/memory/{id}`
- `/api/memory/recall`
- `/api/memory/stats`

职责：

- 读取正式知识库
- 检索图谱、上下文、记忆、统计信息

约定：

- 这是“正式知识只读视图”
- 普通用户永远从这里读，不直接读预审区
- 所有查询默认只返回正式知识，不混入待审核内容

### 5.3 预审提交层

路径前缀：

- `/api/review/*`

当前核心路径：

- `/api/review/memory/submissions`

职责：

- editor 提交新增 / 修改 / 删除的预审请求
- editor 查看、修改、撤回自己的提审单

约定：

- 这是普通用户唯一允许的写入口
- 它写入的是“待审核提交记录”，不是正式知识库
- 非管理员不能通过 `/api/memory/*` 直接修改正式知识

### 5.4 管理审核层

路径前缀：

- `/api/admin/review/*`

职责：

- 管理员查看审核队列
- 审核通过 / 驳回
- 将已通过的预审单发布到正式知识库

约定：

- 审核动作和发布动作分离
- “approve” 表示业务上通过
- “publish” 表示真正写入正式知识库
- 所有审核与发布动作必须保留审计记录

### 5.5 管理员直写层

路径前缀：

- `/api/admin/memory/*`

职责：

- 管理员绕过预审，直接写正式知识库

约定：

- 只允许 `admin`
- 必须强制填写原因
- 必须记录 before / after 审计日志
- 仅用于紧急修复、批量导入、系统校正等管理场景

## 6. 鉴权边界

推荐的默认权限矩阵如下：

| 接口层 | Anonymous | Viewer | Editor | Admin |
|--------|-----------|--------|--------|-------|
| `/api/status` | 可选，仅健康检查 | 可 | 可 | 可 |
| `/api/auth/login` | 可 | 可 | 可 | 可 |
| `/api/auth/register` | 仅引导期可 | 否 | 否 | 否 |
| 正式知识读取层 | 否 | 可 | 可 | 可 |
| 预审提交层 | 否 | 否 | 可 | 可 |
| 管理审核层 | 否 | 否 | 否 | 可 |
| 管理员直写层 | 否 | 否 | 否 | 可 |
| SSE 事件流 | 否 | 按事件范围 | 按事件范围 | 可 |

补充约定：

- 团队部署默认关闭匿名提审
- 正式环境下，除登录与健康检查外，不再开放匿名业务访问
- 管理台页面本身可以公开静态资源，但其数据接口必须受 JWT 保护

## 7. Client 的职责边界

`Client` 不是权限中心，而是本地接入层。

它负责：

- 承载本地 Web UI
- 对 IDE / Agent 暴露 MCP
- 调用 `Server` REST API
- 未来承载本地内置 Agent

它不负责：

- 签发 token
- 决定用户角色
- 绕过 `Server` 权限
- 直接访问正式知识数据库

因此 `Client -> Server` 的关键约定是：

1. 浏览器登录后拿到的 JWT 需要被 `Client` 保存到本地会话，并在转发到 `Server` 时带上 `Authorization: Bearer <token>`
2. `Client` 代理接口要把当前用户 token 继续透传给 `Server`
3. `Client` 上的 MCP 工具也必须绑定一个明确的 `Server` 身份，不能继续以匿名写路径修改正式知识

## 8. SSE 边界与推荐端点

建议新增独立事件流，而不是把流式逻辑混入普通 CRUD 接口。

推荐前缀：

- `/api/events/review`
- `/api/events/agent`
- `/api/events/system`

事件示例：

- `review.submission.created`
- `review.submission.updated`
- `review.submission.approved`
- `review.submission.rejected`
- `review.submission.published`
- `agent.token`
- `agent.completed`
- `system.notice`

鉴权建议：

- SSE 连接与 REST 使用同一套 JWT
- 浏览器端优先使用支持请求头的 `fetch()` 流式读取方案
- 不推荐把长期 JWT 放进 URL 查询参数

## 9. 明确排除项

当前阶段不做：

- 不使用 WebSocket 作为主业务协议
- 不允许普通用户直写正式知识库
- 不把 MCP 暴露为团队共享业务 API
- 不让 `Client` 取代 `Server` 成为权限中心
- 不让预审库内容混入普通查询结果

## 10. 与当前实现的差距

当前代码已经部分对齐这份决策，但还有几个必须收口的点：

1. `Client` 当前调用 `Server` 时还没有统一透传 JWT。
2. `Client Web UI` 目前主要走本地 `/api/...` 代理，需要补齐登录态与 Bearer 注入。
3. `Client` 侧 MCP 的 `remember / update / delete` 仍有旧路径直写 `/api/memory/*` 的情况，需要切到提审流或管理员专用流。
4. `Server` 当前默认允许匿名提审；团队共享部署时应默认关闭。
5. `POST /api/auth/register` 当前是公开可调用的，需要收敛为引导期机制或管理员控制。

## 11. 实施顺序

推荐按下面顺序落地：

1. 先统一 `Server` 的 JWT 规则与角色矩阵
2. 再给 `Client` 增加登录态保存与 token 透传
3. 把 `Client MCP` 写路径切到“editor 走 review、admin 走 direct publish”
4. 给 `Server wwwroot` 管理台补审核列表、详情、审批与发布
5. 最后再补 SSE，用于审核队列刷新和内置 Agent 流式输出

这条顺序的核心目的是：

- 先把权限边界收紧
- 再补用户体验层的实时性

