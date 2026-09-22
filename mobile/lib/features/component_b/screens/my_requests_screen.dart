import 'package:flutter/material.dart';
import '../services/help_request_service.dart';

class MyRequestsScreen extends StatefulWidget {
  const MyRequestsScreen({super.key});

  @override
  State<MyRequestsScreen> createState() => _MyRequestsScreenState();
}

class _MyRequestsScreenState extends State<MyRequestsScreen> {
  List<HelpRequest> _requests = [];
  Map<String, AiPriority> _aiPriorities = {};
  bool _loading = true;
  String? _error;

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    setState(() => _loading = true);
    final loadResult = await HelpRequestService.getMineWithStatus();
    final data = loadResult.requests;
    final priorities = await Future.wait(
      data.map((request) async {
        final priority = await HelpRequestService.getAiPriority(request.id);
        return MapEntry(request.id, priority);
      }),
    );
    if (!mounted) return;
    setState(() {
      _requests = data;
      _error = loadResult.error;
      _aiPriorities = {
        for (final entry in priorities)
          if (entry.value != null) entry.key: entry.value!,
      };
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
            : _error != null
                ? ListView(
                    padding: const EdgeInsets.all(20),
                    children: [
                      _RequestLoadError(message: _error!, onRetry: _load),
                    ],
                  )
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
                    separatorBuilder: (_, _) => const SizedBox(height: 10),
                    itemBuilder: (context, index) {
                      final r = _requests[index];
                      final aiPriority = _aiPriorities[r.id];
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
                                      if (aiPriority != null) ...[
                                        const SizedBox(height: 9),
                                        _AiPriorityBadge(priority: aiPriority),
                                      ],
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

class _RequestLoadError extends StatelessWidget {
  final String message;
  final VoidCallback onRetry;
  const _RequestLoadError({required this.message, required this.onRetry});

  @override
  Widget build(BuildContext context) => Container(
    margin: const EdgeInsets.only(top: 80),
    padding: const EdgeInsets.all(20),
    decoration: BoxDecoration(color: const Color(0xFFFDF0EF), borderRadius: BorderRadius.circular(16)),
    child: Column(children: [
      const Icon(Icons.cloud_off_outlined, color: Color(0xFFC8453C), size: 34),
      const SizedBox(height: 10),
      const Text('Unable to load your requests', style: TextStyle(fontWeight: FontWeight.w700)),
      const SizedBox(height: 5),
      Text(message, textAlign: TextAlign.center, style: const TextStyle(color: Color(0xFF7A7D89), fontSize: 13)),
      const SizedBox(height: 12),
      TextButton.icon(onPressed: onRetry, icon: const Icon(Icons.refresh), label: const Text('Try again')),
    ]),
  );
}

class _AiPriorityBadge extends StatelessWidget {
  final AiPriority priority;

  const _AiPriorityBadge({required this.priority});

  @override
  Widget build(BuildContext context) {
    final color = switch (priority.priority.toLowerCase()) {
      'high' => const Color(0xFFC8453C),
      'medium' => const Color(0xFFB8720A),
      'low' => const Color(0xFF0B6E69),
      _ => const Color(0xFF747783),
    };
    final label = priority.aiAnalysisAvailable ? 'AI priority: ${priority.priority}' : priority.priority;
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 4),
      decoration: BoxDecoration(color: color.withValues(alpha: 0.10), borderRadius: BorderRadius.circular(999)),
      child: Row(mainAxisSize: MainAxisSize.min, children: [
        Icon(priority.aiAnalysisAvailable ? Icons.auto_awesome_outlined : Icons.hourglass_top_outlined, size: 13, color: color),
        const SizedBox(width: 4),
        Flexible(child: Text(label, style: TextStyle(color: color, fontSize: 10, fontWeight: FontWeight.w700), overflow: TextOverflow.ellipsis)),
      ]),
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
  bool _analyzing = false;
  AiRequestAnalysis? _aiAnalysis;
  String? _aiError;

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

  Future<void> _loadAiGuidance() async {
    setState(() {
      _analyzing = true;
      _aiError = null;
    });
    final result = await HelpRequestService.getAiAnalysis(widget.request.id);
    if (!mounted) return;
    setState(() {
      _aiAnalysis = result;
      _analyzing = false;
      _aiError = result == null ? 'AI guidance is unavailable right now. Your request is still being handled.' : null;
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
            if (r.imageUrl != null) ...[
              ClipRRect(
                borderRadius: BorderRadius.circular(14),
                child: Image.network(r.imageUrl!, height: 200, width: double.infinity, fit: BoxFit.cover),
              ),
              const SizedBox(height: 16),
            ],
            Text(r.description, style: const TextStyle(fontSize: 16)),
            const SizedBox(height: 8),
            Text(
              verificationStatusLabels[r.verificationStatus],
              style: const TextStyle(color: Color(0xFF7A7D89), fontSize: 13),
            ),
            const SizedBox(height: 20),
            _AiGuidanceCard(
              analysis: _aiAnalysis,
              loading: _analyzing,
              error: _aiError,
              onRequestAnalysis: _loadAiGuidance,
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

class _AiGuidanceCard extends StatelessWidget {
  final AiRequestAnalysis? analysis;
  final bool loading;
  final String? error;
  final VoidCallback onRequestAnalysis;

  const _AiGuidanceCard({
    required this.analysis,
    required this.loading,
    required this.error,
    required this.onRequestAnalysis,
  });

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.all(16),
      decoration: BoxDecoration(
        color: const Color(0xFFF0F7F7),
        borderRadius: BorderRadius.circular(16),
        border: Border.all(color: const Color(0xFFB9D9D7)),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          const Row(children: [
            Icon(Icons.auto_awesome_outlined, size: 19, color: Color(0xFF0B6E69)),
            SizedBox(width: 8),
            Text('AI guidance', style: TextStyle(fontSize: 15, fontWeight: FontWeight.w700, color: Color(0xFF17323B))),
          ]),
          const SizedBox(height: 7),
          const Text('Supplemental guidance only. Emergency coordinators make the response decisions.', style: TextStyle(fontSize: 12, color: Color(0xFF547071), height: 1.35)),
          if (analysis != null) ...[
            const SizedBox(height: 14),
            Text(analysis!.reasoning, style: const TextStyle(fontSize: 13, height: 1.4)),
            if (analysis!.suggestedAction.isNotEmpty) ...[
              const SizedBox(height: 10),
              const Text('Suggested next step', style: TextStyle(fontSize: 11, fontWeight: FontWeight.w700, color: Color(0xFF547071))),
              const SizedBox(height: 2),
              Text(analysis!.suggestedAction, style: const TextStyle(fontSize: 13, fontWeight: FontWeight.w600, height: 1.35)),
            ],
          ] else ...[
            if (error != null) ...[
              const SizedBox(height: 10),
              Text(error!, style: const TextStyle(fontSize: 12, color: Color(0xFFB33E39))),
            ],
            const SizedBox(height: 12),
            TextButton.icon(
              onPressed: loading ? null : onRequestAnalysis,
              icon: loading
                  ? const SizedBox(width: 16, height: 16, child: CircularProgressIndicator(strokeWidth: 2))
                  : const Icon(Icons.auto_awesome_outlined, size: 18),
              label: Text(loading ? 'Preparing guidance...' : 'Get AI guidance'),
            ),
          ],
        ],
      ),
    );
  }
}
