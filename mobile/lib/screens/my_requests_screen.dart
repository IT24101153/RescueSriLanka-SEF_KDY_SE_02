import 'package:flutter/material.dart';
import '../services/help_request_service.dart';

class MyRequestsScreen extends StatefulWidget {
  const MyRequestsScreen({super.key});

  @override
  State<MyRequestsScreen> createState() => _MyRequestsScreenState();
}

class _MyRequestsScreenState extends State<MyRequestsScreen> {
  List<HelpRequest> _requests = [];
  bool _loading = true;

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    setState(() => _loading = true);
    final data = await HelpRequestService.getMine();
    if (!mounted) return;
    setState(() {
      _requests = data;
      _loading = false;
    });
  }

  Color _statusColor(int status) {
    switch (status) {
      case 3:
        return const Color(0xFF0E8F56); // Resolved
      case 4:
        return const Color(0xFF9A9CA8); // Cancelled
      case 1:
      case 2:
        return const Color(0xFF2F6FB0); // Assigned / In Progress
      default:
        return const Color(0xFFB8720A); // Pending
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: const Text('My Requests')),
      backgroundColor: const Color(0xFFFAFAFB),
      body: RefreshIndicator(
        onRefresh: _load,
        child: _loading
            ? const Center(child: CircularProgressIndicator())
            : _requests.isEmpty
                ? ListView(
                    children: const [
                      SizedBox(height: 120),
                      Center(
                        child: Text(
                          'You haven\'t submitted any requests yet.',
                          style: TextStyle(color: Color(0xFF7A7D89)),
                        ),
                      ),
                    ],
                  )
                : ListView.separated(
                    padding: const EdgeInsets.all(16),
                    itemCount: _requests.length,
                    separatorBuilder: (_, __) => const SizedBox(height: 10),
                    itemBuilder: (context, index) {
                      final r = _requests[index];
                      return Material(
                        color: Colors.white,
                        borderRadius: BorderRadius.circular(14),
                        child: InkWell(
                          borderRadius: BorderRadius.circular(14),
                          onTap: () {
                            Navigator.of(context).push(
                              MaterialPageRoute(builder: (_) => _RequestDetailScreen(request: r)),
                            );
                          },
                          child: Container(
                            padding: const EdgeInsets.all(16),
                            decoration: BoxDecoration(
                              borderRadius: BorderRadius.circular(14),
                              border: Border.all(color: const Color(0xFFE9E9EE)),
                            ),
                            child: Row(
                              children: [
                                Expanded(
                                  child: Column(
                                    crossAxisAlignment: CrossAxisAlignment.start,
                                    children: [
                                      Text(
                                        helpRequestTypeLabels[r.type],
                                        style: const TextStyle(fontWeight: FontWeight.bold, fontSize: 14),
                                      ),
                                      const SizedBox(height: 4),
                                      Text(
                                        r.description,
                                        maxLines: 2,
                                        overflow: TextOverflow.ellipsis,
                                        style: const TextStyle(color: Color(0xFF7A7D89), fontSize: 13),
                                      ),
                                    ],
                                  ),
                                ),
                                const SizedBox(width: 10),
                                Container(
                                  padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 5),
                                  decoration: BoxDecoration(
                                    color: _statusColor(r.status).withValues(alpha: 0.12),
                                    borderRadius: BorderRadius.circular(999),
                                  ),
                                  child: Text(
                                    helpRequestStatusLabels[r.status],
                                    style: TextStyle(
                                      color: _statusColor(r.status),
                                      fontWeight: FontWeight.w600,
                                      fontSize: 11,
                                    ),
                                  ),
                                ),
                              ],
                            ),
                          ),
                        ),
                      );
                    },
                  ),
      ),
    );
  }
}

class _RequestDetailScreen extends StatefulWidget {
  final HelpRequest request;
  const _RequestDetailScreen({required this.request});

  @override
  State<_RequestDetailScreen> createState() => _RequestDetailScreenState();
}

class _RequestDetailScreenState extends State<_RequestDetailScreen> {
  List<StatusHistoryEntry> _history = [];
  bool _loading = true;

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    final data = await HelpRequestService.getHistory(widget.request.id);
    if (!mounted) return;
    setState(() {
      _history = data;
      _loading = false;
    });
  }

  @override
  Widget build(BuildContext context) {
    final r = widget.request;
    return Scaffold(
      appBar: AppBar(title: Text(helpRequestTypeLabels[r.type])),
      backgroundColor: const Color(0xFFFAFAFB),
      body: SafeArea(
        child: ListView(
          padding: const EdgeInsets.all(20),
          children: [
            Text(r.description, style: const TextStyle(fontSize: 16)),
            const SizedBox(height: 8),
            Text(
              verificationStatusLabels[r.verificationStatus],
              style: const TextStyle(color: Color(0xFF7A7D89), fontSize: 13),
            ),
            const SizedBox(height: 24),
            const Text('Status history', style: TextStyle(fontWeight: FontWeight.bold, fontSize: 15)),
            const SizedBox(height: 12),
            if (_loading) const Center(child: CircularProgressIndicator()),
            if (!_loading && _history.isEmpty)
              const Text('No changes recorded yet.', style: TextStyle(color: Color(0xFF7A7D89))),
            ..._history.map(
              (h) => Padding(
                padding: const EdgeInsets.only(bottom: 12),
                child: Row(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Container(
                      margin: const EdgeInsets.only(top: 5, right: 10),
                      width: 8,
                      height: 8,
                      decoration: const BoxDecoration(color: Color(0xFFE8960B), shape: BoxShape.circle),
                    ),
                    Expanded(
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          Text(
                            '${helpRequestStatusLabels[h.oldStatus]} → ${helpRequestStatusLabels[h.newStatus]}',
                            style: const TextStyle(fontWeight: FontWeight.w600, fontSize: 13),
                          ),
                          if (h.notes != null)
                            Text(h.notes!, style: const TextStyle(color: Color(0xFF7A7D89), fontSize: 12)),
                        ],
                      ),
                    ),
                  ],
                ),
              ),
            ),
          ],
        ),
      ),
    );
  }
}